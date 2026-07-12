namespace Workslip.Domain;

public enum JobType
{
    /// <summary>Standard 4V05 job with customer, installation types, control points.</summary>
    /// <remarks>Used as default for existing and new 4V05 related work.</remarks>
    Standard,

    /// <summary>Internal task with no customer, installation types, or control points.</summary>
    Diverse,
    Unknown
}
