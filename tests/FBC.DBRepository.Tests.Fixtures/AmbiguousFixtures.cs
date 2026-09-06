using Microsoft.EntityFrameworkCore;

namespace FBC.DBRepository.Tests.Fixtures;

/// <summary>
/// Two unrelated repositories for one entity, in an assembly of their own.
///
/// <para>They cannot live beside the other test fixtures: every scan of that assembly would then hit
/// this ambiguity and fail, including the tests that check the ordinary path. Keeping them here lets
/// one test scan an assembly that is ambiguous on purpose while the rest scan one that is not.</para>
/// </summary>
public sealed class Doodad : Entity<int, Doodad>
{
    public string Name { get; set; } = "";
}

public sealed class FixturesDbContext(DbContextOptions<FixturesDbContext> options) : DbContext(options)
{
    public DbSet<Doodad> Doodads => Set<Doodad>();
}

public sealed class PrimaryDoodadRepository(FixturesDbContext context)
    : EFRepositoryBase<Doodad, int, FixturesDbContext>(context);

/// <summary>A sibling, not a subclass — this is the case automatic registration must refuse.</summary>
public sealed class SecondaryDoodadRepository(FixturesDbContext context)
    : EFRepositoryBase<Doodad, int, FixturesDbContext>(context);
