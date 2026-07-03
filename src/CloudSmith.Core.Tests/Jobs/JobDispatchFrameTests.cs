// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using CloudSmith.Core.Jobs;
using FluentAssertions;
using Xunit;

namespace CloudSmith.Core.Tests.Jobs;

/// <summary>
/// Serialization round-trip tests for the canonical wire frames
/// (design/api-surface/job-dispatch-contract.md §1, AB#4839).
/// Frames must round-trip through the <see cref="JobFrame"/> polymorphic union with
/// the exact <c>$type</c> discriminators, camelCase property names, and lowercase
/// ack-status strings the Relay and Agent expect on the wire.
/// </summary>
public sealed class JobDispatchFrameTests
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    private static readonly Guid JobId = Guid.Parse("d3b07384-d9a0-4c9e-8f6e-1a2b3c4d5e6f");

    [Fact]
    public void JobDispatch_roundtrips_through_the_polymorphic_union()
    {
        JobFrame frame = new JobDispatch(
            JobId, "cluster.validate-network",
            """{"scriptName":"Validate-Network.ps1","arguments":{"ClusterName":"clu-01"}}""",
            IdempotencyKey: "op-2026-07-03-cluster01-validate",
            Traceparent: "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");

        var json = JsonSerializer.Serialize(frame, Wire);
        json.Should().Contain("\"$type\":\"job.dispatch\"")
            .And.Contain("\"jobId\":\"d3b07384-d9a0-4c9e-8f6e-1a2b3c4d5e6f\"")
            .And.Contain("\"jobType\":\"cluster.validate-network\"")
            .And.Contain("\"payloadJson\":")
            .And.Contain("\"idempotencyKey\":")
            .And.Contain("\"traceparent\":");

        var back = JsonSerializer.Deserialize<JobFrame>(json, Wire);
        back.Should().BeOfType<JobDispatch>().And.BeEquivalentTo(frame);
    }

    [Fact]
    public void JobDispatch_payloadJson_stays_an_opaque_string()
    {
        JobFrame frame = new JobDispatch(JobId, "test.noop", """{"a":1}""");
        var back = (JobDispatch)JsonSerializer.Deserialize<JobFrame>(
            JsonSerializer.Serialize(frame, Wire), Wire)!;

        back.PayloadJson.Should().Be("""{"a":1}""",
            "the dispatch pipeline never interprets the payload (contract §1.1)");
        back.IdempotencyKey.Should().BeNull();
        back.Traceparent.Should().BeNull();
    }

    [Theory]
    [InlineData(JobAckStatus.Accepted,  "accepted")]
    [InlineData(JobAckStatus.Rejected,  "rejected")]
    [InlineData(JobAckStatus.Duplicate, "duplicate")]
    public void JobAck_status_serializes_lowercase_and_roundtrips(JobAckStatus status, string wireValue)
    {
        JobFrame frame = new JobAck(JobId, status,
            status == JobAckStatus.Rejected ? "unknown target agent" : null);

        var json = JsonSerializer.Serialize(frame, Wire);
        json.Should().Contain("\"$type\":\"job.ack\"")
            .And.Contain($"\"ackStatus\":\"{wireValue}\"");

        var back = JsonSerializer.Deserialize<JobFrame>(json, Wire);
        back.Should().BeOfType<JobAck>().And.BeEquivalentTo(frame);
    }

    [Fact]
    public void JobResult_roundtrips_with_all_fields()
    {
        JobFrame frame = new JobResult(
            JobId,
            Succeeded: false,
            ExitCode: 3,
            Output: "partial output before failure",
            Error: "Validate-Network.ps1: cluster unreachable",
            CompletedAt: DateTimeOffset.Parse("2026-07-03T14:21:07.1234560+00:00"));

        var json = JsonSerializer.Serialize(frame, Wire);
        json.Should().Contain("\"$type\":\"job.result\"")
            .And.Contain("\"succeeded\":false")
            .And.Contain("\"exitCode\":3")
            .And.Contain("\"completedAt\":");

        var back = JsonSerializer.Deserialize<JobFrame>(json, Wire);
        back.Should().BeOfType<JobResult>().And.BeEquivalentTo(frame);
    }

    [Fact]
    public void JobResult_output_is_never_null_error_may_be()
    {
        JobFrame frame = new JobResult(JobId, true, 0, Output: "", Error: null,
            CompletedAt: DateTimeOffset.UtcNow);

        var back = (JobResult)JsonSerializer.Deserialize<JobFrame>(
            JsonSerializer.Serialize(frame, Wire), Wire)!;

        back.Output.Should().NotBeNull("output may be empty, never null (contract §1.3)");
        back.Error.Should().BeNull();
    }

    [Fact]
    public void Unknown_discriminator_is_rejected()
    {
        var act = () => JsonSerializer.Deserialize<JobFrame>(
            """{"$type":"job.bogus","jobId":"d3b07384-d9a0-4c9e-8f6e-1a2b3c4d5e6f"}""", Wire);
        act.Should().Throw<JsonException>();
    }
}
