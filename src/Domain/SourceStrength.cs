namespace Portfolio.Domain;

/// <summary>
/// Constitution IV: sources are not equally strong. Strongest first; the order is part
/// of the type (a PostgreSQL enum), not a convention.
/// </summary>
public enum SourceStrength
{
    Statement,
    Export,
    Api,
    Scrape,
    Manual,
}
