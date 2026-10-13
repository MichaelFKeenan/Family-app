using FamilyApp.Modules.Tasks.Data;
using FamilyApp.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FamilyApp.Modules.Tasks;

public sealed class CategoryService(TasksDbContext db, CurrentHousehold household, TimeProvider clock)
{
    public const int MaxName = 50;

    private static readonly string[] Defaults = ["Short term", "Long term", "House"];

    public async Task<IReadOnlyList<CategoryDto>> ListAsync(CancellationToken cancellationToken)
    {
        var categories = await LoadAsync(cancellationToken);
        if (categories.Count > 0) return categories;

        // A new household starts with the default categories, created on first use.
        // ON CONFLICT DO NOTHING makes two simultaneous first visits safe: the unique
        // (household_id, name) constraint lets only one insert of each name succeed.
        // From step 5 this moves to a HouseholdCreated event handler (ADR-004).
        var now = clock.GetUtcNow();
        for (var i = 0; i < Defaults.Length; i++)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO tasks.categories (id, household_id, name, sort_order, created_at)
                VALUES ({Guid.CreateVersion7()}, {household.Id}, {Defaults[i]}, {i}, {now})
                ON CONFLICT (household_id, name) DO NOTHING
                """,
                cancellationToken);
        }

        return await LoadAsync(cancellationToken);
    }

    public async Task<Outcome<CategoryDto>> CreateAsync(CategoryRequest request, CancellationToken cancellationToken)
    {
        if (Validate(request.Name) is { } invalid) return invalid;

        var category = new Category
        {
            Id = Guid.CreateVersion7(),
            Name = request.Name!.Trim(),
            SortOrder = request.SortOrder ?? 0,
            CreatedAt = clock.GetUtcNow(),
        };
        db.Categories.Add(category);

        return await SaveAsync(category, cancellationToken);
    }

    public async Task<Outcome<CategoryDto>> UpdateAsync(Guid id, CategoryRequest request, CancellationToken cancellationToken)
    {
        if (Validate(request.Name) is { } invalid) return invalid;

        var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (category is null) return new Outcome<CategoryDto>.NotFound();

        category.Name = request.Name!.Trim();
        category.SortOrder = request.SortOrder ?? category.SortOrder;
        return await SaveAsync(category, cancellationToken);
    }

    /// <summary>Tasks in a deleted category become uncategorised (the foreign key's SET NULL).</summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Categories.Where(c => c.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;

    private async Task<List<CategoryDto>> LoadAsync(CancellationToken cancellationToken) =>
        await db.Categories
            .AsNoTracking()
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.SortOrder))
            .ToListAsync(cancellationToken);

    private async Task<Outcome<CategoryDto>> SaveAsync(Category category, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return new Outcome<CategoryDto>.Ok(CategoryDto.From(category));
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return new Outcome<CategoryDto>.Conflict($"You already have a category called \"{category.Name}\".");
        }
    }

    private static Outcome<CategoryDto>? Validate(string? name) =>
        string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxName
            ? new Outcome<CategoryDto>.Invalid("name", $"Give the category a name of 1 to {MaxName} characters.")
            : null;
}
