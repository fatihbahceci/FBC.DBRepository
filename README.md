# FBC.DBRepository

A lightweight, generic, async-first repository pattern implementation for Entity Framework Core.

## Features

- **Generic Repository** - `EFRepositoryBase<TEntity, TEntityId, TContext>` works with any entity and DbContext
- **Async-First** - All operations are fully async with `CancellationToken` support
- **Soft Delete** - Built-in soft delete via `IEntityHasSoftDeleteFeature` (automatically filtered from queries)
- **Audit Tracking** - Automatic `CreatedDateUTC`, `UpdatedDateUTC`, `DeletedDateUTC` timestamps
- **User Audit** - Optional `CreatedBy`, `UpdatedBy`, `DeletedBy` tracking via interfaces
- **Pagination** - Built-in pagination with `PaginateResponseModel<T>`
- **Entity Validation** - Pre-operation async validation hooks via `IEntityHasCheckDataFor<TEntity, TId>`
- **Auto DI Registration** - `RegisterRepositories()` scans assemblies and registers repositories automatically

## Installation

```bash
dotnet add package FBC.DBRepository
```

## Quick Start

### 1. Define Your Entity

```csharp
public class Product : Entity<long, Product>,
    IEntityHasSoftDeleteFeature,
    IEntityHasCreatedDate,
    IEntityHasUpdatedDate,
    IEntityHasDeletedDate,
    IEntityHasCreatedBy,
    IEntityHasUpdatedBy
{
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }

    // Interface implementations
    public bool IsDeleted { get; set; }
    public DateTime CreatedDateUTC { get; set; }
    public DateTime? UpdatedDateUTC { get; set; }
    public DateTime? DeletedDateUTC { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
```

### 2. Create Your Repository

```csharp
// Optional: Define a custom interface for additional methods
public interface IProductRepository : IAsyncRepository<Product, long>
{
    Task<IList<Product>> GetExpensiveProductsAsync(decimal minPrice);
}

public class ProductRepository : EFRepositoryBase<Product, long, AppDbContext>, IProductRepository
{
    public ProductRepository(AppDbContext context) : base(context) { }

    // Override to enable user audit tracking
    protected override string? GetCurrentUser() => "current-user-id";

    public async Task<IList<Product>> GetExpensiveProductsAsync(decimal minPrice)
    {
        var result = await GetListAsync(predicate: p => p.Price >= minPrice);
        return result.Items;
    }
}
```

### 3. Register in DI

```csharp
// Program.cs
builder.Services.RegisterRepositories(typeof(ProductRepository).Assembly);
```

This registers both:
- `IAsyncRepository<Product, long>` -> `ProductRepository`
- `IProductRepository` -> `ProductRepository`

### 4. Use in Your Services

```csharp
public class ProductService(IAsyncRepository<Product, long> repo)
{
    public Task<Product?> GetByIdAsync(long id)
        => repo.GetByIdAsync(id);

    public Task<PaginateResponseModel<Product>> GetPagedAsync(int page, int size)
        => repo.GetListAsync(pageNumber: page, itemsPerPage: size);

    public async Task<Product> CreateAsync(Product product)
        => await repo.ApplyOperation(EntityOperation.Create, product, alsoValidate: true);

    public async Task<Product> UpdateAsync(Product product)
        => await repo.ApplyOperation(EntityOperation.Update, product, alsoValidate: true);

    public async Task<Product> SoftDeleteAsync(Product product)
        => await repo.ApplyOperation(EntityOperation.Delete, product, alsoValidate: false);

    public async Task<Product> HardDeleteAsync(Product product)
        => await repo.ApplyOperation(EntityOperation.Delete, product, alsoValidate: false, deletePermanent: true);
}
```

## API Reference

### IAsyncRepository<TEntity, TEntityId>

| Method | Description |
|--------|-------------|
| `GetByIdAsync(id, ...)` | Get a single entity by its primary key |
| `GetAsync(predicate, ...)` | Get a single entity matching a predicate |
| `GetListAsync(predicate, orderBy, include, pageNumber, itemsPerPage, ...)` | Get a paginated list of entities |
| `AnyAsync(predicate, ...)` | Check if any entity matches a predicate |
| `CountAsync(predicate, ...)` | Count entities matching a predicate |
| `ApplyOperation(operationType, entity, alsoValidate, deletePermanent)` | Create, update, or delete an entity |
| `ApplyOperationRange(operationType, entities, alsoValidate, deletePermanent)` | Batch create, update, or delete |

### Entity Interfaces

| Interface | Properties | Applied On |
|-----------|-----------|------------|
| `IEntityHasSoftDeleteFeature` | `IsDeleted` | Delete |
| `IEntityHasCreatedDate` | `CreatedDateUTC` | Create |
| `IEntityHasUpdatedDate` | `UpdatedDateUTC` | Update |
| `IEntityHasDeletedDate` | `DeletedDateUTC` | Delete |
| `IEntityHasCreatedBy` | `CreatedBy` | Create |
| `IEntityHasUpdatedBy` | `UpdatedBy` | Update |
| `IEntityHasDeletedBy` | `DeletedBy` | Delete |

### Entity Validation

Implement `IEntityHasCheckDataFor<TEntity, TId>` on your entity to add pre-operation validation:

```csharp
public class Product : Entity<long, Product>, IEntityHasCheckDataFor<Product, long>
{
    public string Name { get; set; } = string.Empty;

    public async Task CheckDataForAsync(EntityOperation operation, bool alsoValidate, IQueryable<Product> query)
    {
        if (operation == EntityOperation.Create || operation == EntityOperation.Update)
        {
            if (alsoValidate && string.IsNullOrWhiteSpace(Name))
                throw new InvalidOperationException("Product name is required.");

            if (await query.AnyAsync(p => p.Name == Name && !p.Id.Equals(Id)))
                throw new InvalidOperationException("Product name must be unique.");
        }
    }
}
```

### User Audit Tracking

Override `GetCurrentUser()` in your repository to enable automatic user tracking:

```csharp
public class ProductRepository : EFRepositoryBase<Product, long, AppDbContext>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ProductRepository(AppDbContext context, IHttpContextAccessor httpContextAccessor) : base(context)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override string? GetCurrentUser()
        => _httpContextAccessor.HttpContext?.User?.Identity?.Name;
}
```

## Requirements

- .NET 8.0+
- Entity Framework Core 8.0+

## License

MIT
