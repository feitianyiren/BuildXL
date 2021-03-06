// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using BuildXL.Cache.ContentStore.Extensions;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Extensions
{
    public class StringExtensionsTests : TestBase
    {
        public StringExtensionsTests()
            : base(TestGlobal.Logger)
        {
        }

        [Fact]
        public void ToStream()
        {
            using (var stream = "hello".ToUTF8Stream())
            {
                using (var streamReader = new StreamReader(stream))
                {
                    streamReader.ReadToEnd().Should().Be("hello");
                }
            }
        }
    }
}
