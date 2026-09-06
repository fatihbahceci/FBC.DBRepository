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

No application had been bitten by this: the ones that call `RestoreAsync` do not use
`IEntityRequiresRole`, and the one that uses `IEntityRequiresRole` never restores. The gate was
missing rather than deliberately open, which is why it is on by default.

**`CheckRoleRequirement` is `protected virtual`.** The check is skipped entirely when the repository
was built without an `ICurrentUserProvider`. That default has to stay: seeders, migrations and
background jobs have no user to check against, and failing them closed would stop applications from
starting. But it is fail-open, and fail-open is the wrong way round for a repository that serves a
request — so an application can now override the method and refuse instead. The README shows the
override.

### Correctness

**`RestoreAsync` can run `CheckDataForAsync` — opt-in.** A row can stop being valid while it sits
deleted; the ordinary case is a unique value another row has taken meanwhile. Without validation the
restore succeeds as far as the application is concerned and then fails against a database constraint,
so the caller sees a provider exception where a validation message belonged. Pass
`alsoValidate: true`; validation runs as `Update` and *before* the flags are touched, so a refused
restore leaves nothing half-changed.

**It is opt-in because making it the default broke a working application.** That is worth recording
rather than smoothing over. `RestoreAsync` loads the row with no `include`, so its child collections
come back empty — and an entity whose validation covers its children ("an entry must have at least
two lines") then fails *every* restore, because the lines were never loaded. One of the applications
using this library has exactly that rule on the entity it restores. Validation that reads only the
row's own columns is safe here; validation that reaches into a collection is not, and only the caller
knows which kind theirs is.

**Automatic registration no longer chooses at random.** When two types implemented one repository
interface, the first one Reflection happened to return was registered. `Assembly.GetTypes()` makes no
promise about order, so which one won was decided by metadata layout — stable for a given binary, and
free to change the next time the assembly is edited, with nothing to say it had. Unrelated
implementations now throw and name both.

No application is affected: none of them has two repositories for one entity.

**An inheritance chain is not an ambiguity.** A type derived from a repository wins over the one it
derives from. This is the shape of the `CheckRoleRequirement` override above, so reporting it as an
error would have punished exactly the people who took that advice.

**Open generic repositories are skipped by the scan.** A `GenericRepository<TEntity, TId>` picked up
by it was registered against `IAsyncRepository<TEntity, TId>` with unbound type parameters — a
descriptor whose service type is not a closed type and therefore can never satisfy a request. Two
applications keep such a repository inside their unit of work, so both were carrying a dead
registration; skipping them removes it.

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

### Added, part two

These came out of a second pass over the findings, after the first round had been reviewed.

**`GetActiveQueryable()`.** `GetQueryable()` returns soft-deleted rows, because the filter lives in
the query methods rather than in a global query filter. That is what the raw queryable is for, and it
is also the easiest way to get a wrong answer without noticing.

The evidence, stated honestly: **no consuming application had got this wrong.** Only one of them
calls `GetQueryable()`, and both of its uses are deliberate and commented. The mistake that was made
is in this repository — the README's own `CheckDataForAsync` example used the unfiltered queryable to
ask whether a category still had products, so a category whose products had all been soft-deleted
would have refused to be deleted. The addition is for discoverability, not a bug in the field.

Declared on `IAsyncRepository` with a default body so hand-written implementations still compile, and
on `EFRepositoryBase` as well, because a default interface member is only reachable through the
interface and repositories are just as often held by their concrete type.

**`EntityValidationException`.** The validation contract asked entities to throw without saying what,
so every application invented its own type and every endpoint's `catch` caught a different one —
making the line between "the caller sent something invalid" and "something went wrong" a per-project
decision rather than a library one. Nothing in the library throws it and nothing requires it; it
exists so new code has a shared type to agree on. It is not named `ValidationException` because
`System.ComponentModel.DataAnnotations` has one and `Entity<TId, TEntity>` already imports that
namespace for `[Key]` — a second one here would make the name ambiguous (CS0104) in any file
importing both, and existing code would stop compiling.

**`IQuery<T>` is documented.** It was reviewed as dead code, wrongly: `IAsyncRepository<TEntity,
TEntityId>` derives from it, so it is where `GetQueryable()` is declared. What was actually missing is
that the declaration said nothing about the filter it skips — the warning lived in the XML docs of a
different method, which is why one application had written the reminder into its own source instead.

Registering `IQuery<T>` in DI was considered and **rejected**. It would add no capability, only a
narrower dependency, and `IQuery<T>` is not "the read-only repository" — it is "the raw queryable
accessor", the one that does not filter. Making it the easiest thing to inject would advertise a
narrow, safe-looking dependency that quietly returns deleted rows. If read-only segregation is ever
wanted, the right shape is an `IReadOnlyRepository` carrying the four filtered read methods.

### Documentation

**A `docs/BEHAVIOR.md`, and a README that stays practical.** The first round of these changes pushed
long explanations into the README and made it worse at the job it does — showing someone how to use
the library. The reasoning now lives in its own document; the README keeps a one-line warning and a
link wherever a sharp edge exists.

**No page-size ceiling, said out loud.** `itemsPerPage: 0` returns everything and nothing caps the
value. A cap was deliberately not added: it depends on the table, one chosen here would be wrong for
someone, and a silently applied cap is worse than none — a caller who asks for 5000 rows and receives
100 has no way to tell. The warning is now on the parameter, where IntelliSense shows it.

**The README no longer teaches the unfiltered path by accident.** Its `CheckDataForAsync` example used
`GetQueryable()` to look for a category's products, so a category whose products had all been
soft-deleted would have refused to be deleted. It uses `GetActiveQueryable()` now.

**`ToPaginateAsync`: a dead branch removed and the documentation made true.** It claimed that a page
size of zero returns "all items after skipping to the specified page". No code could have done that:
the skip was `pageNumber * itemsPerPage`, which is zero whenever the page size is. The branch did
exactly what the other one did and only made the reader look for a difference. A page size of zero
returns everything in one page and ignores the page number.

**The unbounded page size is now called out.** Nothing caps `itemsPerPage`, because the cap depends
on the table and only the caller knows it — but a page size that arrives from a request and reaches
the method unclamped reads the whole table into memory on one call.

### Tests

The library had none. There are now 34, covering every item above against real SQLite, including the
cases that used to pass silently — restoring without the role, restoring a row that became invalid,
the random registration choice, a write continuing after cancellation — and the compatibility cases
that keep the defaults honest: restore without validation, and the four-argument `ApplyOperation`
call every existing caller uses.

`tests/FBC.DBRepository.Tests.Fixtures` exists for one reason: two deliberately ambiguous
repositories cannot sit beside the other fixtures, or every scan of that assembly would hit them.

---

## 0.4.0

**Breaking:** the third parameter of `IEntityHasCheckDataFor<TEntity, TId>.CheckDataForAsync` changed
from `IQueryable<TEntity>` to `IAsyncRepository<TEntity, TId>`, so validation hooks reach the
repository's own methods — which filter soft-deleted rows — instead of a raw queryable that does not.
See the Breaking Changes section of the README for the before/after.
