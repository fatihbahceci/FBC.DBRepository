# FBC.DBRepository

[![NuGet Badge](https://img.shields.io/nuget/v/FBC.DBRepository.svg?label=NuGet)](https://www.nuget.org/packages/FBC.DBRepository)
[![NuGet Downloads](https://img.shields.io/nuget/dt/FBC.DBRepository.svg)](https://www.nuget.org/packages/FBC.DBRepository)

---

A lightweight, generic, async-first repository pattern implementation for Entity Framework Core. Supports .NET 8, 9 and 10.

## Features

| Feature | Description |
|---------|-------------|
| **Generic Repository** | `EFRepositoryBase<TEntity, TEntityId, TContext>` works with any entity and DbContext |
| **Async-First** | All operations are fully async with `CancellationToken` support |
| **Soft Delete** | Built-in soft delete via `IEntityHasSoftDeleteFeature` (automatically filtered from queries) |
| **Restore** | Restore soft-deleted entities back to active state via `RestoreAsync` |
| **Audit Tracking** | Automatic `CreatedDateUTC`, `UpdatedDateUTC`, `DeletedDateUTC` timestamps |
| **User Audit** | Optional `CreatedBy`, `UpdatedBy`, `DeletedBy` tracking via interfaces |
| **Pagination** | Built-in pagination with `PaginateResponseModel<T>` |
| **Entity Validation** | Pre-operation async validation hooks via `IEntityHasCheckDataFor<TEntity, TId>` |
| **Role-Based Access Control** | Entity-level role checks via `IEntityRequiresRole` + `ICurrentUserProvider` |
| **Transactions** | `BeginTransactionAsync` / `CommitTransactionAsync` / `RollbackTransactionAsync` |
| **Bulk Operations** | `ApplyOperationRange` for batch create, update, or delete |
| **Bulk Query** | `GetByIdsAsync` for retrieving multiple entities by their IDs in a single query |
| **Auto DI Registration** | `RegisterRepositories()` scans assemblies and registers repositories automatically |

## Installation

```bash
dotnet add package FBC.DBRepository
```

## Quick Start

### 1. Define Your Entity

```csharp
public class Product : Entity<int, Product>,
    IEntityHasSoftDeleteFeature,
    IEntityHasCreatedDate,
    IEntityHasUpdatedDate,
    IEntityHasCheckDataFor<Product, int>
{
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }

    // Soft delete
    public bool IsDeleted { get; set; }

    // Audit dates
    public DateTime CreatedDateUTC { get; set; }
    public DateTime? UpdatedDateUTC { get; set; }

    // Validation hook
    public async Task CheckDataForAsync(EntityOperation operation, bool alsoValidate, IAsyncRepository<Product, int> repository)
    {
        if (alsoValidate)
        {
            if (string.IsNullOrWhiteSpace(Name))
                throw new ArgumentException("Product name is required.");

            if (operation == EntityOperation.Create || operation == EntityOperation.Update)
            {
                if (await repository.AnyAsync(p => p.Name == Name && !p.Id.Equals(Id)))
                    throw new ArgumentException("Product name must be unique.");
            }
        }
    }
}
```

### 2. Create Your Repository

```csharp
public class ProductRepository : EFRepositoryBase<Product, int, AppDbContext>
{
    public ProductRepository(AppDbContext context) : base(context) { }
}
```

### 3. Register in DI

```csharp
// Program.cs
builder.Services.RegisterRepositories(typeof(ProductRepository).Assembly);
```

### 4. Use in Your Services

```csharp
public class ProductService(IAsyncRepository<Product, int> repo)
{
    public async Task<Product> CreateAsync(string name, decimal price)
    {
        var product = new Product { Name = name, Price = price };
        return await repo.ApplyOperation(EntityOperation.Create, product, alsoValidate: true);
    }

    public async Task<PaginateResponseModel<Product>> GetPagedAsync(int page, int size)
        => await repo.GetListAsync(pageNumber: page, itemsPerPage: size);

    public async Task SoftDeleteAsync(int id)
    {
        var product = await repo.GetByIdAsync(id)
            ?? throw new KeyNotFoundException();
        await repo.ApplyOperation(EntityOperation.Delete, product, alsoValidate: false);
    }

    public async Task RestoreAsync(int id)
        => await repo.RestoreAsync(id);
}
```

---

## Core Concepts

### Entity Base Class

All entities must inherit from `Entity<TId, TEntity>`:

```csharp
public class Order : Entity<long, Order>
{
    public string OrderNumber { get; set; } = string.Empty;
    public decimal Total { get; set; }
}
```

The base class provides:
- `Id` property (primary key with `[Key]` attribute)
- Automatic `CreatedDateUTC` initialization (if the entity implements `IEntityHasCreatedDate`)
- Internal validation and audit pipeline via `CheckEntityDataForAsync`

### Entity Operations

`EntityOperation` enum controls which operation is being performed:

```csharp
public enum EntityOperation
{
    Create,   // Insert a new entity
    Update,   // Update an existing entity
    Delete    // Soft delete or permanent delete
}
```

Use `ApplyOperation` to perform any CRUD operation:

```csharp
// Create
await repo.ApplyOperation(EntityOperation.Create, entity, alsoValidate: true);

// Update
await repo.ApplyOperation(EntityOperation.Update, entity, alsoValidate: true);

// Soft delete (sets IsDeleted = true)
await repo.ApplyOperation(EntityOperation.Delete, entity, alsoValidate: false);

// Permanent delete (removes from database)
await repo.ApplyOperation(EntityOperation.Delete, entity, alsoValidate: false, deletePermanent: true);
```

---

## Features in Detail

### Soft Delete

Implement `IEntityHasSoftDeleteFeature` to enable soft delete:

```csharp
public class Customer : Entity<int, Customer>, IEntityHasSoftDeleteFeature
{
    public string Name { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
}
```

**How it works:**
- When you call `ApplyOperation(Delete, entity, ...)` without `deletePermanent: true`, the entity's `IsDeleted` is set to `true` instead of being removed from the database.
- All queries via `GetAsync`, `GetListAsync`, `GetByIdAsync`, `AnyAsync`, `CountAsync` automatically exclude soft-deleted records.
- To include deleted records in queries, pass `includeDeletedRecords: true`.

```csharp
// Normal query (excludes deleted)
var activeCustomers = await repo.GetListAsync();

// Include deleted records
var allCustomers = await repo.GetListAsync(includeDeletedRecords: true);
```

### Restore (Soft Delete Recovery)

Restore a soft-deleted entity back to active state:

```csharp
// Restore by ID
var restoredCustomer = await repo.RestoreAsync(customerId);
```

**What `RestoreAsync` does:**
- Finds the entity (including deleted records)
- Sets `IsDeleted` to `false`
- Clears `DeletedDateUTC` and `DeletedBy` (if implemented)
- Updates `UpdatedDateUTC` and `UpdatedBy` (if implemented)
- Saves the changes

### Audit Tracking

Implement audit interfaces to automatically track timestamps and users:

```csharp
public class Invoice : Entity<int, Invoice>,
    IEntityHasCreatedDate,   // CreatedDateUTC set on Create
    IEntityHasUpdatedDate,   // UpdatedDateUTC set on Update
    IEntityHasDeletedDate,   // DeletedDateUTC set on Delete
    IEntityHasCreatedBy,     // CreatedBy set on Create
    IEntityHasUpdatedBy,     // UpdatedBy set on Update
    IEntityHasDeletedBy      // DeletedBy set on Delete
{
    public string InvoiceNumber { get; set; } = string.Empty;

    // Audit timestamps
    public DateTime CreatedDateUTC { get; set; }
    public DateTime? UpdatedDateUTC { get; set; }
    public DateTime? DeletedDateUTC { get; set; }

    // Audit users
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
    public string? DeletedBy { get; set; }
}
```

**Automatic behavior per operation:**

| Operation | Fields Set |
|-----------|-----------|
| Create | `CreatedDateUTC` = UtcNow, `CreatedBy` = current user |
| Update | `UpdatedDateUTC` = UtcNow, `UpdatedBy` = current user |
| Soft Delete | `IsDeleted` = true, `DeletedDateUTC` = UtcNow, `DeletedBy` = current user |
| Permanent Delete | Only `CheckDataForAsync` is called (entity is removed) |

### User Audit (GetCurrentUser)

To enable user tracking, either override `GetCurrentUser()` or inject `ICurrentUserProvider`:

**Option 1: Override in repository**

```csharp
public class InvoiceRepository : EFRepositoryBase<Invoice, int, AppDbContext>
{
    private readonly IHttpContextAccessor _http;

    public InvoiceRepository(AppDbContext context, IHttpContextAccessor http) : base(context)
    {
        _http = http;
    }

    protected override string? GetCurrentUser()
        => _http.HttpContext?.User?.FindFirst("UserId")?.Value;
}
```

**Option 2: Use ICurrentUserProvider (recommended)**

```csharp
// Implement the provider
public class CurrentUserProvider(IHttpContextAccessor http) : ICurrentUserProvider
{
    public string? GetUserId()
        => http.HttpContext?.User?.FindFirst("UserId")?.Value;

    public string? GetUserName()
        => http.HttpContext?.User?.Identity?.Name;

    public string[] GetRoles()
        => http.HttpContext?.User?.Claims
            .Where(c => c.Type == ClaimTypes.Role)
            .Select(c => c.Value)
            .ToArray() ?? [];

    public bool IsInRole(string role)
        => http.HttpContext?.User?.IsInRole(role) ?? false;
}

// Register in DI
builder.Services.AddScoped<ICurrentUserProvider, CurrentUserProvider>();

// Use in repository (provider is injected via constructor)
public class InvoiceRepository : EFRepositoryBase<Invoice, int, AppDbContext>
{
    public InvoiceRepository(AppDbContext context, ICurrentUserProvider provider)
        : base(context, provider) { }
}
```

When `ICurrentUserProvider` is passed to the repository, `GetCurrentUser()` automatically returns the provider's `GetUserId()` value. You can still override `GetCurrentUser()` for custom logic.

### Entity Validation (CheckDataForAsync)

Implement `IEntityHasCheckDataFor<TEntity, TId>` to add pre-operation validation and data adjustment:

```csharp
public class Category : Entity<int, Category>,
    IEntityHasCheckDataFor<Category, int>
{
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }

    public async Task CheckDataForAsync(EntityOperation operation, bool alsoValidate, IAsyncRepository<Category, int> repository)
    {
        // Data adjustment (always runs)
        Slug = Name.ToLower().Replace(" ", "-");

        // Validation (only when alsoValidate is true)
        if (alsoValidate)
        {
            if (string.IsNullOrWhiteSpace(Name))
                throw new ArgumentException("Category name is required.");

            switch (operation)
            {
                case EntityOperation.Create:
                case EntityOperation.Update:
                    if (await repository.AnyAsync(c => c.Name == Name && !c.Id.Equals(Id)))
                        throw new ArgumentException("Category name must be unique.");
                    break;

                case EntityOperation.Delete:
                    var hasProducts = await repository.GetQueryable()
                        .Where(c => c.Id.Equals(Id))
                        .SelectMany(c => c.Products)
                        .AnyAsync();
                    if (hasProducts)
                        throw new InvalidOperationException("Cannot delete a category with products.");
                    break;
            }
        }
    }
}
```

**Key points:**
- `CheckDataForAsync` is called automatically by `ApplyOperation` before the database operation.
- Use it for both **data adjustment** (normalizing values, syncing fields) and **validation** (uniqueness checks, business rules).
- The `repository` parameter gives you access to the full repository capabilities (`AnyAsync`, `GetAsync`, `GetListAsync`, `CountAsync`, etc.) for cross-entity checks.
- If you need raw `IQueryable<TEntity>` access, you can call `repository.GetQueryable()`.
- **Recommended:** Prefer using the repository methods (`AnyAsync`, `GetAsync`, `GetListAsync`) over `GetQueryable()` — especially if your entity implements `IEntityHasSoftDeleteFeature`, because repository methods automatically filter out soft-deleted records from every query.
- The `alsoValidate` parameter lets you skip validation when you only want data adjustment.

### Role-Based Access Control (RBAC)

Enforce entity-level role checks using `IEntityRequiresRole` + `ICurrentUserProvider`:

**Step 1: Define required roles on your entity**

```csharp
public class Device : Entity<int, Device>, IEntityRequiresRole
{
    public string Name { get; set; } = string.Empty;

    public string[] GetRequiredRolesFor(EntityOperation operation) => operation switch
    {
        EntityOperation.Create => ["Edit.Devices"],
        EntityOperation.Update => ["Edit.Devices"],
        EntityOperation.Delete => ["Edit.Devices", "SysAdmin"],
        _ => []
    };
}
```

**Step 2: Ensure ICurrentUserProvider is injected into your repository**

```csharp
public class DeviceRepository : EFRepositoryBase<Device, int, AppDbContext>
{
    public DeviceRepository(AppDbContext context, ICurrentUserProvider provider)
        : base(context, provider) { }
}
```

**How it works:**
- Before every `ApplyOperation` / `ApplyOperationRange`, the repository checks if the entity implements `IEntityRequiresRole`.
- If it does and an `ICurrentUserProvider` is available, it calls `GetRequiredRolesFor(operation)`.
- If the user has **any** of the required roles, the operation proceeds.
- If the user has **none** of the required roles, an `UnauthorizedAccessException` is thrown.
- If no `ICurrentUserProvider` is injected, role checks are skipped (backward compatible).

**This provides defense-in-depth:** even if an API endpoint accidentally lacks authorization attributes, the repository layer catches unauthorized operations.

### Pagination

All list queries return `PaginateResponseModel<T>`:

```csharp
// Paginated query (page 0, 10 items per page)
var result = await repo.GetListAsync(
    predicate: p => p.IsActive,
    orderBy: q => q.OrderBy(p => p.Name),
    pageNumber: 0,
    itemsPerPage: 10,
    enableTracking: false
);

// Access pagination metadata
int totalItems = result.TotalFilteredCount;
int totalPages = result.TotalPages;
bool hasNext = result.HasNext;
bool hasPrevious = result.HasPrevious;
IList<Product> items = result.Items;
```

**Special cases:**
- `itemsPerPage = 0` returns **all records** in a single page.
- `pageNumber` is zero-based (0 = first page).

### Transactions

Wrap multiple operations in a single atomic unit:

```csharp
public async Task TransferDeviceAsync(int deviceId, int newGroupId)
{
    await repo.BeginTransactionAsync();
    try
    {
        var device = await repo.GetByIdAsync(deviceId)
            ?? throw new KeyNotFoundException();

        device.DeviceGroupId = newGroupId;
        await repo.ApplyOperation(EntityOperation.Update, device, alsoValidate: true);

        // More operations within the same transaction...

        await repo.CommitTransactionAsync();
    }
    catch
    {
        await repo.RollbackTransactionAsync();
        throw;
    }
}
```

**Key points:**
- Only one transaction can be active at a time per repository instance.
- `CommitTransactionAsync` saves all changes; `RollbackTransactionAsync` discards them.
- An `InvalidOperationException` is thrown if you try to begin a second transaction or commit/rollback without an active transaction.

### Bulk Operations

Use `ApplyOperationRange` for batch operations:

```csharp
// Batch create
var newProducts = new List<Product> { product1, product2, product3 };
await repo.ApplyOperationRange(EntityOperation.Create, newProducts, alsoValidate: true);

// Batch delete
var toDelete = existingProducts.Where(p => p.IsExpired).ToList();
await repo.ApplyOperationRange(EntityOperation.Delete, toDelete, alsoValidate: false, deletePermanent: true);
```

### Bulk Query (GetByIdsAsync)

Retrieve multiple entities by their IDs in a single database query:

```csharp
var ids = new[] { 1, 5, 12, 23 };
var products = await repo.GetByIdsAsync(ids, enableTracking: false);
```

Supports all standard options: `include`, `enableTracking`, `includeDeletedRecords`.

### Eager Loading (Include)

Use the `include` parameter to load related entities:

```csharp
// Single include
var device = await repo.GetByIdAsync(id,
    include: q => q.Include(d => d.DeviceType));

// Multiple includes with ThenInclude
var device = await repo.GetByIdAsync(id,
    include: q => q
        .Include(d => d.DeviceType)
        .Include(d => d.DeviceGroup)
        .Include(d => d.Addresses).ThenInclude(a => a.AddrType));
```

### Change Tracking Control

Disable EF Core change tracking for read-only queries to improve performance:

```csharp
// Read-only (no tracking)
var products = await repo.GetListAsync(enableTracking: false);

// Tracked (needed if you plan to update)
var product = await repo.GetByIdAsync(id, enableTracking: true);
```

### Auto DI Registration

`RegisterRepositories` scans assemblies and registers all repository implementations:

```csharp
// Program.cs
builder.Services.RegisterRepositories(typeof(ProductRepository).Assembly);
```

This registers both:
- `IAsyncRepository<Product, int>` -> `ProductRepository` (base interface)
- `IProductRepository` -> `ProductRepository` (custom interface, if exists)

**Custom interface example:**

```csharp
public interface IProductRepository : IAsyncRepository<Product, int>
{
    Task<IList<Product>> GetExpensiveProductsAsync(decimal minPrice);
}

public class ProductRepository : EFRepositoryBase<Product, int, AppDbContext>, IProductRepository
{
    public ProductRepository(AppDbContext context) : base(context) { }

    public async Task<IList<Product>> GetExpensiveProductsAsync(decimal minPrice)
    {
        var result = await GetListAsync(predicate: p => p.Price >= minPrice);
        return result.Items;
    }
}
```

---

## API Reference

### IAsyncRepository<TEntity, TEntityId>

| Method | Description |
|--------|-------------|
| `GetByIdAsync(id, ...)` | Get a single entity by its primary key |
| `GetByIdsAsync(ids, ...)` | Get multiple entities by their primary keys in a single query |
| `GetAsync(predicate, ...)` | Get a single entity matching a predicate |
| `GetListAsync(predicate, orderBy, include, pageNumber, itemsPerPage, ...)` | Get a paginated list of entities |
| `GetListAsync(query, ...)` | Get a paginated list from a pre-built IQueryable |
| `AnyAsync(predicate, ...)` | Check if any entity matches a predicate |
| `CountAsync(predicate, ...)` | Count entities matching a predicate |
| `ApplyOperation(operationType, entity, alsoValidate, deletePermanent)` | Create, update, or delete a single entity |
| `ApplyOperationRange(operationType, entities, alsoValidate, deletePermanent)` | Batch create, update, or delete multiple entities |
| `RestoreAsync(id)` | Restore a soft-deleted entity |
| `BeginTransactionAsync()` | Begin a database transaction |
| `CommitTransactionAsync()` | Commit the current transaction |
| `RollbackTransactionAsync()` | Rollback the current transaction |

### Entity Marker Interfaces

| Interface | Property | Auto-Set On |
|-----------|----------|-------------|
| `IEntityHasSoftDeleteFeature` | `bool IsDeleted` | Delete (soft) |
| `IEntityHasCreatedDate` | `DateTime CreatedDateUTC` | Create |
| `IEntityHasUpdatedDate` | `DateTime? UpdatedDateUTC` | Update |
| `IEntityHasDeletedDate` | `DateTime? DeletedDateUTC` | Delete (soft) |
| `IEntityHasCreatedBy` | `string? CreatedBy` | Create |
| `IEntityHasUpdatedBy` | `string? UpdatedBy` | Update |
| `IEntityHasDeletedBy` | `string? DeletedBy` | Delete (soft) |

### Behavior Interfaces

| Interface | Purpose |
|-----------|---------|
| `IEntityHasCheckDataFor<TEntity, TId>` | Pre-operation data adjustment and validation |
| `IEntityRequiresRole` | Entity-level role-based access control |
| `ICurrentUserProvider` | Provides current user identity and roles |

### PaginateResponseModel<T>

| Property | Type | Description |
|----------|------|-------------|
| `Items` | `IList<T>` | Items for the current page |
| `PageIndex` | `int` | Zero-based current page index |
| `ItemsPerPage` | `int` | Number of items per page |
| `TotalFilteredCount` | `int` | Total matching items across all pages |
| `TotalPages` | `int` | Total number of pages |
| `HasPrevious` | `bool` | True if there is a previous page |
| `HasNext` | `bool` | True if there is a next page |

## Breaking Changes

### V.0.4.0: `IEntityHasCheckDataFor<TEntity, TId>.CheckDataForAsync` — parameter change

The third parameter of `CheckDataForAsync` has been changed from `IQueryable<TEntity>` to `IAsyncRepository<TEntity, TId>`.

**Before:**
```csharp
public async Task CheckDataForAsync(EntityOperation operation, bool alsoValidate, IQueryable<Product> query)
{
    if (await query.AnyAsync(p => p.Name == Name && !p.Id.Equals(Id)))
        throw new ArgumentException("Product name must be unique.");
}
```

**After:**
```csharp
public async Task CheckDataForAsync(EntityOperation operation, bool alsoValidate, IAsyncRepository<Product, int> repository)
{
    if (await repository.AnyAsync(p => p.Name == Name && !p.Id.Equals(Id)))
        throw new ArgumentException("Product name must be unique.");
}
```

**Migration guide:**
- Replace `IQueryable<TEntity>` with `IAsyncRepository<TEntity, TId>` in your `CheckDataForAsync` implementations.
- If you were using `query.AnyAsync(...)`, `query.Where(...)`, etc., prefer using the equivalent repository methods (`repository.AnyAsync(...)`, `repository.GetAsync(...)`, `repository.GetListAsync(...)`) instead. This is especially recommended if your entity implements `IEntityHasSoftDeleteFeature`, because repository methods automatically exclude soft-deleted records from every query.
- If you still need raw `IQueryable<TEntity>` access, call `repository.GetQueryable()`.

---

## Requirements

- .NET 8.0, 9.0, or 10.0
- Entity Framework Core (version matched to your .NET target)

## License

MIT
