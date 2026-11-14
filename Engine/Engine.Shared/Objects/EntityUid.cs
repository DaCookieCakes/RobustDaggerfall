namespace Engine.Shared.Objects;

/// <summary>
///    The unique identifier for a given entity.
/// </summary>
/// <param name="Id">The given ID for an entity.</param>
public readonly record struct EntityUid(int Id)
{
    /// <summary>
    ///     An invalid UID.
    ///     This is used to compare IDs against.
    /// </summary>
    public static readonly EntityUid Invalid = new(0);

    /// <summary>
    ///     The first UID that the EntityManager should use.
    /// </summary>
    public static readonly EntityUid FirstUid = new(1);

    /// <summary>
    ///     Checks if the ID value is valid at all.
    /// </summary>
    /// <returns></returns>
    public bool IsValid() => Id > Invalid.Id;

    /// <inheritdoc/>
    public override string ToString() => Id.ToString();
}
