/// <summary>
/// De mogelijke statussen van een validatieresultaat.
/// </summary>
public enum ValidationStatus
{
    /// <summary>
    /// De validatie is doorlopen zonder fouten of waarschuwingen.
    /// </summary>
    Passing,

    /// <summary>
    /// Er is een fout gevonden die aandacht vereist, maar het document is niet direct onbruikbaar.
    /// </summary>
    Failing,

    /// <summary>
    /// Er is een waarschuwing gevonden die de kwaliteit beïnvloedt, maar het document kan nog worden verwerkt.
    /// </summary>
    Warning,

    /// <summary>
    /// Informatieboodschap, geen fout of waarschuwing.
    /// </summary>
    Info
}

/// <summary>
/// De mogelijke ernstniveaus van een validatiefout.
/// </summary>
public enum ValidationSeverity
{
    /// <summary>
    /// Kritiek probleem dat automatische verwerking verhindert.
    /// </summary>
    Critical,

    /// <summary>
    /// Foutieve of onbetrouwbare gegevens.
    /// </summary>
    Error,

    /// <summary>
    /// Mogelijk probleem, maar kan nog steeds worden verwerkt met voorzichtigheid.
    /// </summary>
    Warning,

    /// <summary>
    /// Normale informatie, geen probleem.
    /// </summary>
    Info
}