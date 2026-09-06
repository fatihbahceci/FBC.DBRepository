# Behaviour notes

The README shows how to use the library. This file explains the parts that surprise people, and why
they are the way they are.

Everything here is a decision someone can disagree with. Where a choice has a cost, the cost is
written down rather than left for the reader to discover.

- [Soft delete and the two queryables](#soft-delete-and-the-two-queryables)
- [Role checks and the fail-open default](#role-checks-and-the-fail-open-default)
- [What restore does, and why](#what-restore-does-and-why)
- [Automatic registration](#automatic-registration)
- [Transactions belong to the DbContext](#transactions-belong-to-the-dbcontext)
- [Pagination has no ceiling](#pagination-has-no-ceiling)
- [Cancellation on the write path](#cancellation-on-the-write-path)
- [Which exception means what](#which-exception-means-what)
- [Sharp edges that are staying](#sharp-edges-that-are-staying)

---

## Soft delete and the two queryables

Soft delete is **not** an EF global query filter here. The filter is applied inside the query methods
instead — `GetAsync`, `GetListAsync`, `GetByIdAsync`, `AnyAsync`, `CountAsync` — and
`includeDeletedRecords: true` turns it off for one call.

The consequence is one line in `EFRepositoryBase`:

```csharp
public IQueryable<TEntity> GetQueryable() => _context.Set<TEntity>();
```

**`GetQueryable()` returns deleted rows.** That is what it is for — sometimes you want them — but it
is also the easiest way to get a wrong answer without noticing, because nothing fails. Four routes,
in order of preference:

| Want | Use |
|---|---|
| Ordinary reads | `GetAsync` / `GetListAsync` / `AnyAsync` / `CountAsync` |
| A query those cannot express, filtered | `GetActiveQueryable()` |
| A hand-built query, paginated and filtered | `GetListAsync(query, …)` — it filters on the way through |
| Deleted rows deliberately | `GetQueryable()`, and say so in a comment |

`GetActiveQueryable()` was added in 0.5.0 as a convenience, and it is worth being straight about the
evidence: **no application using this library had got it wrong.** Only one of them calls
`GetQueryable()` at all, and both of its uses are deliberate — one wants the deleted rows, the other
hands the queryable to `GetListAsync(query, …)`, which re-applies the filter. Each carries a comment
saying so.

The mistake that *was* made is in this repository. The README's own `CheckDataForAsync` example used
`GetQueryable()` to ask whether a category still had products, so a category whose products had all
been soft-deleted would have refused to be deleted. If the documentation gets it wrong in its worked
example, the method is easy enough to reach for by accident — but the honest reason for the addition
is discoverability, not a bug anyone has hit.

**Why not a global query filter.** It would make the default safe, but `IgnoreQueryFilters()` is
all-or-nothing: turning off the soft-delete filter also turns off every other filter on the entity.
One of the audited applications went that way and ended up with dozens of `IgnoreQueryFilters()`
calls, each of which had to be read carefully to see what else it was disabling. **Do not use both
mechanisms in one project** — pick this one or `HasQueryFilter`, and hold to it.

---

## Role checks and the fail-open default

An entity that implements `IEntityRequiresRole` declares which roles each operation needs.
`ApplyOperation` and `ApplyOperationRange` check it, and `RestoreAsync` does too since 0.5.0.

```csharp
if (entity is IEntityRequiresRole roleEntity && _currentUserProvider != null)
```

**With no `ICurrentUserProvider`, nothing is checked and nothing is said.** A repository built as
`new ProductRepository(context)` enforces none of the entity's rules.

That default has to stay. Seeders, migrations, background jobs, maintenance commands and tests all
construct repositories with no user, and there is nothing sensible to check them against — failing
them closed would stop applications from starting.

It is still fail-open, and fail-open is the wrong way round for a repository that serves a request.
So since 0.5.0 the check is `protected virtual`, and an application whose repositories always run
with a user can refuse instead:

```csharp
public abstract class StrictRepositoryBase<TEntity, TId, TContext>(TContext context, ICurrentUserProvider? user)
    : EFRepositoryBase<TEntity, TId, TContext>(context, user)
    where TEntity : Entity<TId, TEntity>
    where TId : IEquatable<TId>
    where TContext : DbContext
{
    protected override void CheckRoleRequirement(EntityOperation operation, TEntity entity)
    {
        if (entity is IEntityRequiresRole && _currentUserProvider is null)
            throw new InvalidOperationException(
                $"{typeof(TEntity).Name} declares required roles but this repository has no user provider.");

        base.CheckRoleRequirement(operation, entity);
    }
}
```

Deriving from a repository like this is a supported pattern, and automatic registration knows it: a
derived type wins over the one it derives from rather than being reported as an ambiguity.

**This is defence in depth, not the front door.** The repository catching an unauthorised write is a
second line behind the endpoint's own authorization. It is not a replacement for it: the check needs
a user provider to do anything at all, and the entity decides the rules from data it holds, not from
the request.

---

## What restore does, and why

```csharp
var restored = await repo.RestoreAsync(id);
```

In order:

1. Loads the row, including deleted ones.
2. **Checks the roles required for `EntityOperation.Delete`.**
3. Returns early if the row is not deleted.
4. **Runs `CheckDataForAsync(EntityOperation.Update, alsoValidate: true, …)`.**
5. Clears `IsDeleted`, `DeletedDateUTC`, `DeletedBy`; sets `UpdatedDateUTC`, `UpdatedBy`.
6. Saves.

Steps 2 and 4 are new in 0.5.0. Both were missing, and both mattered.

**Why the `Delete` role.** Restoring is the inverse of deleting, so it must not be the weaker gate of
the two. Where only an Owner may delete a row, an Editor must not be able to bring it back — which is
exactly what happened before.

**Why the check comes before the early return.** Otherwise an unauthorised caller could tell a
deleted row from a live one by whether the call threw.

**Why validation runs as `Update`, not `Delete`.** The entity is being written, so `Update` is what
its hook should see. Running it as `Delete` would trigger rules like "cannot delete a category that
has products" — the opposite of what is happening.

**Why validation runs at all.** A row can stop being valid while it sits deleted; the ordinary case
is a unique value another row has taken meanwhile. Before 0.5.0 the restore succeeded as far as the
application was concerned and then failed against a database constraint, so the caller saw a provider
exception where a validation message belonged.

**Why validation runs before the flags are touched.** A refused restore must leave the tracked entity
exactly as it was, or a later `SaveChanges` elsewhere in the same unit of work would persist a
half-restored row.

---

## Automatic registration

`RegisterRepositories(assemblies)` scans and registers, with a scoped lifetime:

- the closed `IAsyncRepository<TEntity, TEntityId>` for every repository found;
- any non-generic interface deriving from it (`IProductRepository`) against its implementation;
- the repository classes themselves, **only** when `includeConcreteTypes: true`.

### Why concrete types are opt-in

A repository class with no interface of its own can only be injected as
`IAsyncRepository<Product, int>`. Injecting `ProductRepository` — which is what you do when it
carries queries the generic interface does not — used to fail at resolution time with a message
naming the *handler* that wanted it rather than the missing registration, and only where DI
validation was enabled. In production it stayed hidden until the first request.

It is opt-in rather than automatic because registering every repository twice is a change to what a
container holds, and the flag makes it a decision rather than a surprise.

### Ambiguity is refused

Before 0.5.0, two implementations of one repository interface meant one of them won — whichever
Reflection returned first. Type order is not stable across builds, so the same source could resolve
differently from one compile to the next and nothing said so.

Unrelated implementations now throw and name both. Register the one you want explicitly.

**An inheritance chain is not an ambiguity.** `CachedProductRepository : ProductRepository` registers
the derived type. Anything else would punish the `CheckRoleRequirement` override above, which is a
recommended pattern in this same library.

### Open generics are skipped

A `GenericRepository<TEntity, TId>` caught by the scan would be registered against
`IAsyncRepository<TEntity, TId>` with unbound type parameters, and every entity's resolution would
break. Keep such a type private — a nested class inside a unit of work is the usual shape — or accept
that the scan passes it by.

### A type that fails to load no longer stops the scan

`Assembly.GetTypes()` throws when any single type cannot be loaded, and a missing optional dependency
is enough. With no assembly named, the scan falls back to everything loaded in the AppDomain, so one
unrelated assembly could take startup down with it. The types that did load are used instead.

### Why `IQuery<T>` is not registered

`IAsyncRepository<TEntity, TEntityId>` derives from `IQuery<TEntity>`, so `GetQueryable()` is
available to everyone already; registering `IQuery<Product>` separately would add no capability, only
the ability to declare a narrower dependency.

It was considered and rejected. `IQuery<T>` is not "the read-only repository" — it is "the raw
queryable accessor", and the raw queryable is the one that does **not** filter deleted rows. Making
it the easiest thing to inject would advertise a narrow, safe-looking dependency that quietly returns
deleted data.

If read-only segregation is ever genuinely wanted, the right shape is an `IReadOnlyRepository` that
carries the four filtered read methods, and `IAsyncRepository` extends it. That is a larger change to
the contract and nobody has needed it yet.

---

## Transactions belong to the DbContext

`BeginTransactionAsync` starts a transaction on the repository's `DbContext`, not on the repository.
That difference shows as soon as two repositories share a context — the unit-of-work arrangement, and
the usual way to write two entities atomically:

```csharp
await nodes.BeginTransactionAsync();          // starts it on the shared DbContext

await nodes.ApplyOperation(EntityOperation.Update, node, alsoValidate: true);
await edges.ApplyOperationRange(EntityOperation.Create, newEdges, alsoValidate: false);
//    ^ a different repository, same context: this write is inside the same transaction

await nodes.CommitTransactionAsync();         // only the repository that began it can commit
```

- `edges` writes inside the transaction automatically, because its `SaveChanges` goes through the
  same context.
- `edges.BeginTransactionAsync()` throws: one is already running on that context.
- `edges.CommitTransactionAsync()` throws as well — it did not start it.

Both of those threw before 0.5.0 too, but with EF's generic message. They now say which of the two
situations you are in. `CurrentTransaction` and `HasActiveTransaction` report the transaction on the
context, whoever began it.

**Why this matters more than it looks.** `ApplyOperation` calls its own `SaveChangesAsync`. Two writes
that must land together are two `SaveChanges` calls, and without an explicit transaction they are two
separate ones — a crash between them leaves the first written and the second not.

---

## Pagination has no ceiling

`itemsPerPage: 0` returns every matching row in a single page, and `pageNumber` is ignored.

Nothing caps the page size, and **nothing here will**. A cap depends on the table, and a cap the
library picked would be wrong for someone; worse, a silently applied one means a caller who asks for
5000 rows and receives 100 has no way to tell. Clamp it where the value enters your application:

```csharp
size = Math.Clamp(size, 1, MaxPageSize);   // and do not let 0 through from outside
```

An unclamped page size that arrives from a request is a way to read an entire table into memory with
one call.

---

## Cancellation on the write path

Every read has taken a `CancellationToken` since the first version. The writes did not, so a
cancelled request still wrote. Since 0.5.0:

```csharp
await repo.ApplyOperation(EntityOperation.Create, entity, alsoValidate: true,
                          deletePermanent: false, cancellationToken: ct);
```

**The token is required on the new overloads, not optional.** An optional one would make the existing
four-argument call ambiguous between the two overloads, and current code would stop compiling.

They are declared on `IAsyncRepository` with a default body so that a hand-written implementation of
the interface still compiles. That body forwards to the tokenless overload and therefore **ignores
the token** — `EFRepositoryBase` overrides it and honours it properly. If you implement the interface
yourself, override both.

---

## Which exception means what

| Exception | Means |
|---|---|
| `UnauthorizedAccessException` | The current user lacks a role the entity requires |
| `KeyNotFoundException` | `RestoreAsync` was given an id that does not exist |
| `InvalidOperationException` | A programming error: restoring an entity without soft delete, misusing transactions, an ambiguous registration |
| `EntityValidationException` | The entity refused its own data — yours to throw, from `CheckDataForAsync` |

`EntityValidationException` was added in 0.5.0 and **nothing in the library throws it**. The
validation contract had always asked entities to throw without saying what, so every application
invented its own type and every endpoint's `catch` caught a different one — which made the line
between "the caller sent something invalid" (400) and "something went wrong" (500) a per-project
decision instead of a library one.

It is not named `ValidationException` on purpose: that name is taken by
`System.ComponentModel.DataAnnotations`, which `Entity<TId, TEntity>` already imports for `[Key]`. A
second one in this namespace would make the name ambiguous — CS0104 — in any file importing both, and
existing code would stop compiling.

Nothing requires it. An entity may keep throwing whatever it throws today.

---

## Sharp edges that are staying

Known, deliberate, and not scheduled to change.

**`ApplyOperation(Update, …)` marks the whole graph.** It calls `context.Update(entity)`, which marks
every reachable tracked entity as `Modified`. If a form posts back both a foreign key and the
navigation object, the create path fails on a unique constraint and the update path **silently
overwrites** the related row's audit columns. Detach the navigations the entity does not own before
writing.

**The validation hook runs for the root entity only.** `ApplyOperation(op, parent, alsoValidate: true)`
does not call `CheckDataForAsync` on `parent.Children`, so anything derived there — a search text, a
slug, a total — is saved empty. Put derived-field logic in a `Derive()` method the writing code calls
as well.

**`ApplyOperationRange` is not a set-based update.** It walks the collection, checks and validates
each entity, then issues one `SaveChanges`. It is a batch, not a single SQL statement.

**Permanent delete skips the audit stamps.** `deletePermanent: true` runs `CheckDataForAsync` and then
removes the row; there is nothing left to stamp.
