namespace FBC.DBRepository;

/// <summary>
/// Provides current user identity and role information for audit tracking and role-based access control.
/// Implement this interface and register it in DI to enable automatic user audit fields and entity-level role checks.
/// </summary>
public interface ICurrentUserProvider
{
    /// <summary>
    /// Returns the current user's unique identifier (e.g., user ID from claims).
    /// Used for CreatedBy, UpdatedBy, DeletedBy audit fields.
    /// </summary>
    string? GetUserId();

    /// <summary>
    /// Returns the current user's display name or username.
    /// </summary>
    string? GetUserName();

    /// <summary>
    /// Returns all roles assigned to the current user.
    /// </summary>
    string[] GetRoles();

    /// <summary>
    /// Checks if the current user has a specific role.
    /// </summary>
    bool IsInRole(string role);
}
