namespace DocumentProcessing.Domain.Enums;

/// <summary>
/// Status van een documentreview door een menselijke beoordelaar.
/// </summary>
public enum ReviewStatus
{
    /// <summary>
    /// Document wacht op menselijke beoordeling.
    /// </summary>
    Pending,

    /// <summary>
    /// Een beoordelaar heeft waarden gecorrigeerd, maar nog niet goedgekeurd of afgewezen.
    /// </summary>
    Corrected,

    /// <summary>
    /// Document is goedgekeurd door een bevoegde beoordelaar.
    /// </summary>
    Approved,

    /// <summary>
    /// Document is afgewezen door een bevoegde beoordelaar.
    /// </summary>
    Rejected
}
