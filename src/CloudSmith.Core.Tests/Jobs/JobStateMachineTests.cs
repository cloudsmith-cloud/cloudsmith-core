// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

using CloudSmith.Core.Jobs;
using FluentAssertions;
using Xunit;

namespace CloudSmith.Core.Tests.Jobs;

/// <summary>
/// Exhaustive verification of the canonical legal-transition table
/// (design/api-surface/job-dispatch-contract.md §2, AB#4839).
/// Every (from, to) pair over the full status set is asserted — legal and illegal.
/// </summary>
public sealed class JobStateMachineTests
{
    /// <summary>The exact legal set per contract §2. Anything not listed is illegal.</summary>
    private static readonly HashSet<(string From, string To)> ExpectedLegal =
    [
        (JobStatuses.Queued,     JobStatuses.Dispatched),
        (JobStatuses.Queued,     JobStatuses.Failed),
        (JobStatuses.Queued,     JobStatuses.Cancelled),
        (JobStatuses.Dispatched, JobStatuses.Queued),
        (JobStatuses.Dispatched, JobStatuses.Running),
        (JobStatuses.Dispatched, JobStatuses.Failed),
        (JobStatuses.Dispatched, JobStatuses.TimedOut),
        (JobStatuses.Dispatched, JobStatuses.Cancelled),
        (JobStatuses.Running,    JobStatuses.Succeeded),
        (JobStatuses.Running,    JobStatuses.Failed),
        (JobStatuses.Running,    JobStatuses.TimedOut),
    ];

    public static TheoryData<string, string, bool> FullMatrix()
    {
        var data = new TheoryData<string, string, bool>();
        foreach (var from in JobStatuses.All)
            foreach (var to in JobStatuses.All)
                data.Add(from, to, ExpectedLegal.Contains((from, to)));
        return data;
    }

    [Theory]
    [MemberData(nameof(FullMatrix))]
    public void CanTransition_matches_the_frozen_contract_table(string from, string to, bool expected) =>
        JobStateMachine.CanTransition(from, to).Should().Be(expected,
            $"contract §2 defines '{from}' → '{to}' as {(expected ? "legal" : "illegal")}");

    [Fact]
    public void Legal_transition_count_is_exactly_eleven() =>
        JobStateMachine.LegalTransitions.Should().HaveCount(11)
            .And.BeEquivalentTo(ExpectedLegal);

    [Theory]
    [InlineData("succeeded")]
    [InlineData("failed")]
    [InlineData("timed_out")]
    [InlineData("cancelled")]
    public void Terminal_states_have_no_outbound_transitions(string terminal)
    {
        JobStatuses.IsTerminal(terminal).Should().BeTrue();
        foreach (var to in JobStatuses.All)
            JobStateMachine.CanTransition(terminal, to).Should().BeFalse(
                $"'{terminal}' is terminal — no transitions out, ever (contract §2)");
    }

    [Theory]
    [InlineData("queued")]
    [InlineData("dispatched")]
    [InlineData("running")]
    public void Non_terminal_states_are_not_terminal(string status) =>
        JobStatuses.IsTerminal(status).Should().BeFalse();

    [Fact]
    public void Self_transitions_are_always_illegal()
    {
        foreach (var status in JobStatuses.All)
            JobStateMachine.CanTransition(status, status).Should().BeFalse();
    }

    [Fact]
    public void Unknown_states_never_transition()
    {
        JobStateMachine.CanTransition("bogus", JobStatuses.Running).Should().BeFalse();
        JobStateMachine.CanTransition(JobStatuses.Queued, "bogus").Should().BeFalse();
    }

    [Fact]
    public void Running_cannot_be_cancelled_in_wave_1() =>
        // Contract §2 note ³ — no kill channel to the PS7 subprocess yet.
        JobStateMachine.CanTransition(JobStatuses.Running, JobStatuses.Cancelled).Should().BeFalse();
}
