namespace Backend.Shared.DevSeed;

public interface IDevDataSeeder
{
    Task SeedAsync(CancellationToken ct = default);
}
