namespace Accession.Core.Settings;

/// <summary>How byte sizes are shown in the UI and in exports. The database always stores bytes.</summary>
public enum SizeUnitSystem
{
    /// <summary>KB, MB, GB, TB – powers of 1000.</summary>
    Decimal = 0,

    /// <summary>KiB, MiB, GiB, TiB – powers of 1024.</summary>
    Binary = 1,
}
