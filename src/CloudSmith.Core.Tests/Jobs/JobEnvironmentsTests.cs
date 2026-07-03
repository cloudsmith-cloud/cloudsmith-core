// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

using CloudSmith.Core.Jobs;
using FluentAssertions;
using Xunit;

namespace CloudSmith.Core.Tests.Jobs;

/// <summary>
/// Verifies the env normalization rule (design/api-surface/job-dispatch-contract.md §3, AB#4839):
/// core.jobs.env is NOT NULL DEFAULT 'default', so a caller-supplied null/empty/whitespace
/// env MUST coerce to 'default' at create time and never be written as NULL.
/// </summary>
public sealed class JobEnvironmentsTests
{
    [Fact]
    public void Default_is_the_contract_literal() =>
        JobEnvironments.Default.Should().Be("default",
            "contract §3 defines core.jobs.env as text NOT NULL DEFAULT 'default'");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("  \r\n  ")]
    public void Null_or_blank_env_coerces_to_default(string? env) =>
        JobEnvironments.Normalize(env).Should().Be(JobEnvironments.Default,
            "a create with no env must land on 'default', never NULL (contract §3)");

    [Theory]
    [InlineData("default")]
    [InlineData("prod")]
    [InlineData("staging")]
    [InlineData("Lab-01")]
    public void Explicit_env_passes_through_unchanged(string env) =>
        JobEnvironments.Normalize(env).Should().Be(env,
            "routing compares (site_id, env) strictly (contract §5) — no case folding or trimming");
}
