// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

namespace CloudSmith.Core.Substrate;

/// <summary>Substrate the CloudSmith platform is running on.</summary>
public enum SubstrateMode
{
    /// <summary>Azure Container Apps (PaaS) deployment.</summary>
    PaaS,

    /// <summary>Standalone on-premises Docker Compose deployment.</summary>
    OnPrem,

    /// <summary>Appliance image — on-prem variant with pre-bundled images.</summary>
    Appliance,
}

/// <summary>Artifact classification for <see cref="ISubstrateAdapter.WriteOperatorArtifactAsync"/>.</summary>
public enum ArtifactKind
{
    /// <summary>Sensitive secret material (token, key). Written to KV or restricted file.</summary>
    Secret,

    /// <summary>Diagnostic output (log dump, health snapshot). Written to observability backend.</summary>
    Diagnostic,

    /// <summary>Immutable deployment receipt (timestamps, versions). Retained for audit trail.</summary>
    Receipt,
}

/// <summary>Substrate-specific host metadata surfaced in health responses.</summary>
public sealed record HostInfo(
    string? RevisionOrHostname,
    string? Region,
    string? ResourceGroup);
