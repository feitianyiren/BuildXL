﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.Monitor.App.Scheduling;
using BuildXL.Cache.Monitor.Library.Rules;
using Kusto.Data.Common;
using static BuildXL.Cache.Monitor.App.Analysis.Utilities;

namespace BuildXL.Cache.Monitor.App.Rules.Kusto
{
    internal class LastRestoredCheckpointRule : MultipleStampRuleBase
    {
        public class Configuration : MultiStampRuleConfiguration
        {
            public Configuration(MultiStampRuleConfiguration kustoRuleConfiguration)
                : base(kustoRuleConfiguration)
            {
            }

            public TimeSpan LookbackPeriod { get; set; } = TimeSpan.FromHours(2);

            public TimeSpan ActivityPeriod { get; set; } = TimeSpan.FromHours(1);

            public TimeSpan MasterActivityPeriod { get; set; } = TimeSpan.FromHours(1);

            public Thresholds<int> MissingRestoreMachinesThresholds = new Thresholds<int>()
            {
                Info = 5,
                Warning = 10,
                Error = 20,
                Fatal = 50,
            };

            public Thresholds<int> OldRestoreMachinesThresholds = new Thresholds<int>()
            {
                Info = 5,
                Warning = 10,
                Error = 20,
                Fatal = 50,
            };

            public TimeSpan CheckpointAgeErrorThreshold { get; set; } = TimeSpan.FromMinutes(45);
        }

        private readonly Configuration _configuration;

        /// <inheritdoc />
        public override string Identifier => $"{nameof(LastRestoredCheckpointRule)}:{_configuration.Environment}";

        public LastRestoredCheckpointRule(Configuration configuration)
            : base(configuration)
        {
            _configuration = configuration;
        }

#pragma warning disable CS0649
        internal class Result
        {
            public string Machine = string.Empty;
            public DateTime LastActivityTime;
            public DateTime? LastRestoreTime;
            public TimeSpan? Age;
            public string Stamp = string.Empty;
        }
#pragma warning restore CS0649

        public override async Task Run(RuleContext context)
        {
            var now = _configuration.Clock.UtcNow;
            var query =
                $@"
                let end = now();
                let start = end - {CslTimeSpanLiteral.AsCslString(_configuration.LookbackPeriod)};
                let activity = end - {CslTimeSpanLiteral.AsCslString(_configuration.ActivityPeriod)};
                let masterActivity = end - {CslTimeSpanLiteral.AsCslString(_configuration.MasterActivityPeriod)};
                let Events = table('{_configuration.CacheTableName}')
                | where PreciseTimeStamp between (start .. end);
                let Machines = Events
                | where PreciseTimeStamp >= activity
                | summarize LastActivityTime=max(PreciseTimeStamp) by Machine, Stamp
                | where not(isnull(Machine));
                let CurrentMaster = Events
                | where PreciseTimeStamp >= masterActivity
                | where Role == 'Master'
                | where Operation == 'CreateCheckpointAsync' and isnotempty(Duration)
                | where Result == '{Constants.ResultCode.Success}'
                | summarize (PreciseTimeStamp, Master)=arg_max(PreciseTimeStamp, Machine)
                | project-away PreciseTimeStamp
                | where not(isnull(Master));
                let MachinesWithRole = CurrentMaster
                | join hint.strategy=broadcast kind=rightouter Machines on $left.Master == $right.Machine
                | extend IsWorkerMachine=iif(isnull(Master) or isempty(Master), true, false)
                | project-away Master;
                let Restores = Events
                | where Operation == 'RestoreCheckpointAsync' and isnotempty(Duration)
                | where Result == '{Constants.ResultCode.Success}'
                | summarize LastRestoreTime=max(PreciseTimeStamp) by Machine;
                MachinesWithRole
                | where IsWorkerMachine
                | project-away IsWorkerMachine
                | join hint.strategy=broadcast kind=leftouter Restores on Machine
                | project-away Machine1
                | extend Age=LastActivityTime - LastRestoreTime";
            var results = (await QueryKustoAsync<Result>(context, query)).ToList();

            GroupByStampAndCallHelper<Result>(results, result => result.Stamp, lastRestoredCheckpointHelper);

            void lastRestoredCheckpointHelper(string stamp, List<Result> results)
            {
                if (results.Count == 0)
                {
                    Emit(context, "NoLogs", Severity.Fatal,
                        $"No machines logged anything in the last `{_configuration.ActivityPeriod}`",
                        stamp,
                        eventTimeUtc: now);
                    return;
                }

                var missing = new List<Result>();
                var failures = new List<Result>();
                foreach (var result in results)
                {
                    if (!result.LastRestoreTime.HasValue)
                    {
                        missing.Add(result);
                        continue;
                    }

                    Contract.AssertNotNull(result.Age);
                    if (result.Age.Value >= _configuration.CheckpointAgeErrorThreshold)
                    {
                        failures.Add(result);
                    }
                }

                _configuration.MissingRestoreMachinesThresholds.Check(missing.Count, (severity, threshold) =>
                {
                    var formattedMissing = missing.Select(m => $"`{m.Machine}`");
                    var machinesCsv = string.Join(", ", formattedMissing);
                    var shortMachinesCsv = string.Join(", ", formattedMissing.Take(5));
                    Emit(context, "NoRestoresThreshold", severity,
                        $"Found `{missing.Count}` machine(s) active in the last `{_configuration.ActivityPeriod}`, but without checkpoints restored in at least `{_configuration.LookbackPeriod}`: {machinesCsv}",
                        stamp,
                        $"`{missing.Count}` machine(s) haven't restored checkpoints in at least `{_configuration.LookbackPeriod}`. Examples: {shortMachinesCsv}",
                        eventTimeUtc: now);
                });

                _configuration.OldRestoreMachinesThresholds.Check(failures.Count, (severity, threshold) =>
                {
                    var formattedFailures = failures.Select(f => $"`{f.Machine}` ({f.Age ?? TimeSpan.Zero})");
                    var machinesCsv = string.Join(", ", formattedFailures);
                    var shortMachinesCsv = string.Join(", ", formattedFailures.Take(5));
                    Emit(context, "OldRestores", severity,
                        $"Found `{failures.Count}` machine(s) active in the last `{_configuration.ActivityPeriod}`, but with old checkpoints (at least `{_configuration.CheckpointAgeErrorThreshold}`): {machinesCsv}",
                        stamp,
                        $"`{failures.Count}` machine(s) have checkpoints older than `{_configuration.CheckpointAgeErrorThreshold}`. Examples: {shortMachinesCsv}",
                        eventTimeUtc: now);
                });
            }
        }
    }
}
