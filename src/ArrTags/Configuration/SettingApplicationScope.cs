namespace ArrTags.Configuration;

/// <summary>
/// The ADR-028 clause 2 binary application scope of one user-adjustable setting.
/// A setting is <see cref="RestartRequired"/> when any consumer resolves its
/// value at singleton construction, and <see cref="PerOperation"/> only when
/// every consumer resolves it from the current configuration snapshot on each
/// operation. A setting with both kinds of consumer is classified
/// restart-required and flagged mixed by
/// <see cref="SettingClassification.IsMixed"/>.
/// </summary>
public enum SettingApplicationScope
{
    /// <summary>
    /// Every consumer reads the value from the current configuration snapshot on
    /// each operation, so a saved replacement takes effect without a restart.
    /// </summary>
    PerOperation = 0,

    /// <summary>
    /// At least one consumer captures the value when a singleton is constructed,
    /// so a saved replacement changes that consumer only after a host restart.
    /// </summary>
    RestartRequired = 1,
}
