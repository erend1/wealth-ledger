namespace WealthLedger.Domain.Evidence
{
    /// <summary>
    /// The closed set of retained attachment formats.
    /// </summary>
    /// <remarks>
    /// Active or opaque formats (SVG, HTML, scripts, executables, Office
    /// macro containers, arbitrary binary) have no member and therefore can
    /// never be retained.
    /// </remarks>
    public enum EvidenceMediaType
    {
        Pdf,
        Png,
        Jpeg,
        PlainText,
        Csv
    }
}
