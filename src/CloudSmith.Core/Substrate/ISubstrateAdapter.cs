// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

namespace CloudSmith.Core.Substrate;

/// <summary>
/// AB#2354 — Single abstraction for all substrate-asymmetric operations (PaaS vs on-prem vs appliance).
/// Register the concrete implementation in DI based on CLOUDSMITH_DEPLOYMENT_MODE.
/// All code that previously branched on _isPaaS must use this interface instead.
/// </summary>
public interface ISubstrateAdapter
{
    /// <summary>Which substrate this adapter represents.</summary>
    SubstrateMode Mode { get; }

    // ---- Secrets ------------------------------------------------------------

    /// <summary>
    /// Reads a secret by logical name.
    /// PaaS → Azure Key Vault. On-prem → restricted file under /etc/cloudsmith/secrets/.
    /// Returns null when the secret does not exist.
    /// </summary>
    Task<string?> GetSecretAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Writes or updates a secret by logical name with an optional expiry.
    /// PaaS → Azure Key Vault. On-prem → restricted file (mode 600 / ACL-restricted).
    /// </summary>
    Task SetSecretAsync(string name, string value, DateTimeOffset? expiresOn = null, CancellationToken ct = default);

    /// <summary>
    /// Deletes a secret by logical name. No-op when the secret does not exist.
    /// </summary>
    Task DeleteSecretAsync(string name, CancellationToken ct = default);

    // ---- Operator artifacts -------------------------------------------------

    /// <summary>
    /// Writes an operator-facing artifact to the substrate's canonical location.
    /// PaaS: Secret → KV secret (with optional TTL); Diagnostic → App Insights event; Receipt → KV tagged secret.
    /// On-prem: Secret → restricted file; Diagnostic → Loki; Receipt → /var/lib/cloudsmith/receipts/.
    /// </summary>
    Task WriteOperatorArtifactAsync(string logicalName, string content, ArtifactKind kind, DateTimeOffset? expiresOn = null, CancellationToken ct = default);

    /// <summary>
    /// Returns the substrate-specific CLI command or file path an operator uses to
    /// retrieve an artifact (e.g. "az keyvault secret show …" on PaaS, file path on-prem).
    /// </summary>
    string GetOperatorRetrievalHint(string logicalName);

    // ---- Platform lifecycle -------------------------------------------------

    /// <summary>
    /// Triggers a platform image update.
    /// PaaS → ACA revision swap via ARM SDK. On-prem → SignalR broadcast to runner agents.
    /// </summary>
    Task TriggerImageUpdateAsync(string imageRef, CancellationToken ct = default);

    // ---- Host info ----------------------------------------------------------

    /// <summary>Returns substrate-specific host metadata for health endpoint enrichment.</summary>
    Task<HostInfo> GetHostInfoAsync(CancellationToken ct = default);
}
