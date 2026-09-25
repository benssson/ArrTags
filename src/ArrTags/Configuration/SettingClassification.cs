using System;
using System.Collections.Generic;

namespace ArrTags.Configuration;

/// <summary>
/// One user-adjustable setting classified under ADR-028 clause 2. The
/// classification is binary (<see cref="SettingApplicationScope"/>) and is
/// recorded with per-consumer code evidence: the singleton-construction capture
/// sites (if any) and the per-operation snapshot-resolution sites (if any). A
/// setting with both is restart-required and <see cref="IsMixed"/> is set, so
/// the settings page can state that some paths apply the change immediately
/// while others require a restart. The complete set is owned by
/// <see cref="RestartRequiredSettings"/>; this type only carries one entry.
/// </summary>
public sealed class SettingClassification
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SettingClassification"/> class
    /// and enforces the ADR-028 clause 2 binary rule: a mixed setting must be
    /// restart-required, a per-operation setting must have no construction-capture
    /// evidence, and a restart-required setting must have construction-capture
    /// evidence unless it is explicitly recorded as having no runtime consumer.
    /// </summary>
    /// <param name="name">The stable code-owned setting name.</param>
    /// <param name="scope">The binary application scope.</param>
    /// <param name="isMixed">Whether both kinds of consumer exist.</param>
    /// <param name="pageElementIds">The settings-page element ids for the setting.</param>
    /// <param name="constructionCapturedEvidence">The singleton-construction capture sites.</param>
    /// <param name="perOperationEvidence">The per-operation snapshot-resolution sites.</param>
    /// <param name="note">An optional audit note (for example, the audited reason a setting has no runtime consumer).</param>
    /// <exception cref="ArgumentException">The name is empty or the scope/invariants are inconsistent.</exception>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public SettingClassification(
        string name,
        SettingApplicationScope scope,
        bool isMixed,
        IReadOnlyList<string> pageElementIds,
        IReadOnlyList<string> constructionCapturedEvidence,
        IReadOnlyList<string> perOperationEvidence,
        string? note = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(pageElementIds);
        ArgumentNullException.ThrowIfNull(constructionCapturedEvidence);
        ArgumentNullException.ThrowIfNull(perOperationEvidence);

        if (!Enum.IsDefined(scope))
        {
            throw new ArgumentOutOfRangeException(nameof(scope), scope, "The application scope must be a defined value.");
        }

        if (isMixed && scope != SettingApplicationScope.RestartRequired)
        {
            throw new ArgumentException(
                "A setting with both a construction-captured and a per-operation consumer is restart-required (ADR-028 clause 2).",
                nameof(isMixed));
        }

        if (scope == SettingApplicationScope.PerOperation && constructionCapturedEvidence.Count != 0)
        {
            throw new ArgumentException(
                "A per-operation setting must have no consumer that resolves the value at singleton construction (ADR-028 clause 2).",
                nameof(constructionCapturedEvidence));
        }

        if (scope == SettingApplicationScope.RestartRequired
            && constructionCapturedEvidence.Count == 0
            && string.IsNullOrEmpty(note))
        {
            throw new ArgumentException(
                "A restart-required setting requires construction-capture evidence or a recorded no-runtime-consumer note.",
                nameof(constructionCapturedEvidence));
        }

        Name = name;
        Scope = scope;
        IsMixed = isMixed;
        PageElementIds = pageElementIds;
        ConstructionCapturedEvidence = constructionCapturedEvidence;
        PerOperationEvidence = perOperationEvidence;
        Note = note;
    }

    /// <summary>
    /// Gets the stable, code-owned setting name (<c>Type.Property</c>).
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the binary application scope (ADR-028 clause 2).
    /// </summary>
    public SettingApplicationScope Scope { get; }

    /// <summary>
    /// Gets a value indicating whether the setting has both a
    /// singleton-construction-captured and a per-operation consumer, so its
    /// settings-page note states that some paths apply the change immediately.
    /// </summary>
    public bool IsMixed { get; }

    /// <summary>
    /// Gets a value indicating whether the setting is in the authoritative
    /// restart-required set.
    /// </summary>
    public bool RestartRequired => Scope == SettingApplicationScope.RestartRequired;

    /// <summary>
    /// Gets the settings-page element ids that surface this setting. A setting
    /// repeated per connection or per selector lists every concrete element id.
    /// </summary>
    public IReadOnlyList<string> PageElementIds { get; }

    /// <summary>
    /// Gets the code-evidenced singleton-construction capture sites. Each entry
    /// names the resolving file, type/member, and the capture it performs.
    /// </summary>
    public IReadOnlyList<string> ConstructionCapturedEvidence { get; }

    /// <summary>
    /// Gets the code-evidenced per-operation snapshot-resolution sites. Each
    /// entry names the resolving file, type/member, and where the current
    /// snapshot is read.
    /// </summary>
    public IReadOnlyList<string> PerOperationEvidence { get; }

    /// <summary>
    /// Gets the optional audit note. The only current use is the audited inert
    /// global request-timeout limit, which has no runtime consumer at all.
    /// </summary>
    public string? Note { get; }

    /// <summary>
    /// Gets a value indicating whether at least one runtime consumer resolves the
    /// setting (from the snapshot or at singleton construction). The audited inert
    /// global request-timeout limit is the only setting with no runtime consumer.
    /// </summary>
    public bool HasRuntimeConsumer => ConstructionCapturedEvidence.Count > 0 || PerOperationEvidence.Count > 0;
}
