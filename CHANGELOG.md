# Changelog

All notable changes to FBC.DBRepository. Newest first.

The reasoning is kept alongside each entry on purpose: a line saying *what* changed is enough to
upgrade, but not enough to decide whether to keep the change when it gets in the way later.

---

## 0.5.0

A review pass over the whole surface. Nothing here needs a source change to adopt; three items
change behaviour, and all three replace a silent wrong answer with a visible one.

### Security

**`RestoreAsync` now checks roles.** It ran no role check at all, so `Delete` was gated and its
inverse was not: where only an Owner could delete a row, an Editor could bring it back. It is gated
by the roles `GetRequiredRolesFor(EntityOperation.Delete)` returns — the inverse of an operation must
not be the weaker of the two — and the check happens before the early return for an already-active
row, so an unauthorised caller cannot use it to learn whether a row is deleted.

**`CheckRoleRequirement` is `protected virtual`.** The check is skipped entirely when the repository
was built without an `ICurrentUserProvider`. That default has to stay: seeders, migrations and
background jobs have no user to check against, and failing them closed would stop applications from
starting. But it is fail-open, and fail-open is the wrong way round for a repository that serves a
request — so an application can now override the method and refuse instead. The README shows the
override.

### Correctness

**`RestoreAsync` now runs `CheckDataForAsync`.** A row can stop being valid while it sits deleted;
the ordinary case is a unique value that another row has taken meanwhile. The restore used to
succeed as far as the application was concerned and then fail against a database constraint, so the
caller saw a provider exception where a validation message belonged. Validation runs *before* the
flags are touched, so a refused restore leaves nothing half-changed for a later `SaveChanges` to
persist.

**Automatic registration no longer chooses at random.** When two types implemented one repository
interface, the first one Reflection happened to return was registered. Type order is not stable
across builds, so the same source could resolve differently from one compile to the next and nothing
said so. Unrelated implementations now throw and name both.

**An inheritance chain is not an ambiguity.** A type derived from a repository wins over the one it
derives from. This is the shape of the `CheckRoleRequirement` override above, so reporting it as an
error would have punished exactly the people who took that advice.

**Open generic repositories are skipped by the scan.** A `GenericRepository<TEntity, TId>` picked up
by it was registered against `IAsyncRepository<TEntity, TId>` with unbound type parameters, which
breaks resolution for every entity in the application.

**A type that fails to load no longer stops the scan.** `Assembly.GetTypes()` throws when any single
type cannot be loaded — a missing optional dependency is enough. With no assembly named the scan
falls back to everything in the AppDomain, so one unrelated assembly could take startup down with it.
The types that did load are used instead.

### Added

**Cancellation on the write path.** `ApplyOperation` and `ApplyOperationRange` have overloads taking
a `CancellationToken`, passed through to `SaveChangesAsync`. Every read has taken a token since the
first version; the writes did not, so a cancelled request still wrote. The token is **required** on
the new overloads rather than optional — an optional one would make the existing four-argument call
ambiguous and stop current code from compiling. They are declared on `IAsyncRepository` with a
default body so that a hand-written implementation of the interface still compiles; that body
forwards to the tokenless overload, and `EFRepositoryBase` overrides it to honour the token.

**`RegisterRepositories(includeConcreteTypes: true, …)`.** A repository class with no interface of
its own could only be injected as `IAsyncRepository<TEntity, TId>`. Injecting the concrete type —
which is what you do when the repository carries queries the generic interface does not — failed at
resolution time with a message naming the handler rather than the missing registration, and only
where DI validation was enabled. Defaults to `false`, which is what every earlier version did.

**`CurrentTransaction` and `HasActiveTransaction`.** A transaction belongs to the `DbContext`, not to
the repository, so repositories sharing a context share one transaction and only the one that began
it can commit. That is the unit-of-work arrangement and it worked already, but the API said nothing
about it and the errors came from EF. Beginning a second transaction on the same context, or
committing from a repository that did not start it, now says which of the two situations you are in.

### Documentation

**`ToPaginateAsync`: a dead branch removed and the documentation made true.** It claimed that a page
size of zero returns "all items after skipping to the specified page". No code could have done that:
the skip was `pageNumber * itemsPerPage`, which is zero whenever the page size is. The branch did
exactly what the other one did and only made the reader look for a difference. A page size of zero
returns everything in one page and ignores the page number.

**The unbounded page size is now called out.** Nothing caps `itemsPerPage`, because the cap depends
on the table and only the caller knows it — but a page size that arrives from a request and reaches
the method unclamped reads the whole table into memory on one call.

### Tests

The library had none. There is now an MSTest project covering every item above against real SQLite,
including the cases that used to pass silently: restoring without the role, restoring a row that
became invalid, the random registration choice, and a write continuing after cancellation.

`tests/FBC.DBRepository.Tests.Fixtures` exists for one reason: two deliberately ambiguous
repositories cannot sit beside the other fixtures, or every scan of that assembly would hit them.

---

## 0.4.0

**Breaking:** the third parameter of `IEntityHasCheckDataFor<TEntity, TId>.CheckDataForAsync` changed
from `IQueryable<TEntity>` to `IAsyncRepository<TEntity, TId>`, so validation hooks reach the
repository's own methods — which filter soft-deleted rows — instead of a raw queryable that does not.
See the Breaking Changes section of the README for the before/after.
