# Copilot Prompt: Build an EF6 Database-First Data Layer for .NET Framework 4.7.2

You are helping me build a **.NET Framework 4.7.2** data layer based on an **existing relational database schema**.

The architecture must use **Entity Framework 6.x**, **Database First**, and an `.edmx` model. The goal is to generate clean POCO entity classes into a dedicated class library that has **no dependency on EntityFramework.dll**, while keeping all EF-specific infrastructure in a separate data-access project.

## High-Level Goal

Build a solution structure similar to this:

```text
MyApp.Entities
    Generated POCO entity classes
    Partial classes for safe extensions
    No EntityFramework package reference
    No DbContext
    No EF-specific attributes if avoidable

MyApp.Data.EF6
    .edmx model
    DbContext
    T4 templates
    EF6 package reference
    connection string / mapping
    Database First update workflow

MyApp.Application
    uses MyApp.Entities
    optionally references MyApp.Data.EF6 through repository/service abstractions

MyApp.Tests
    integration-ish tests where practical
```

## Target Framework and Packages

Use:

```text
.NET Framework 4.7.2
Entity Framework 6.x
Database First / EDMX
DbContext generator
T4 templates
```

Do **not** use EF Core.

## Core Architectural Requirements

### 1. Database First from Existing Database

Use the existing database schema as the source of truth.

Create or configure an `.edmx` model using EF6 Database First.

The update workflow should be:

```text
Update Model from Database
→ Save EDMX
→ Regenerate DbContext and POCOs through T4
→ No manual copy/paste of generated entity classes
```

### 2. Dedicated POCO Entity Assembly

Generated entity classes must be emitted into a dedicated class library, for example:

```text
MyApp.Entities
```

This project must not reference Entity Framework.

The generated entities should be ordinary POCO classes such as:

```csharp
public partial class Person
{
    public Guid PersonId { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public DateTime BirthDate { get; set; }

    public ICollection<Address> Addresses { get; set; }
}
```

Avoid generated dependencies on:

```text
EntityObject
EntityCollection<T>
EntityReference<T>
ObjectContext
DbContext
EntityFramework.dll
```

### 3. T4 Template Modification

Configure or modify the EF6 T4 templates so that:

```text
Model.tt
    generates entity classes into MyApp.Entities

Model.Context.tt
    generates DbContext into MyApp.Data.EF6
```

The entity T4 template must generate files into the entity project, not into the EF data project.

Do not require manual moving of generated files after schema updates.

Use `partial` classes so hand-written code can live safely beside generated code.

Generated files should be clearly marked as generated.

Hand-written extensions should be placed in separate files such as:

```text
Person.partial.cs
Address.partial.cs
```

### 4. No Lazy Loading

Lazy loading must be disabled.

In the generated or partial DbContext, ensure:

```csharp
Configuration.LazyLoadingEnabled = false;
```

The code should rely on explicit loading or eager loading with `Include`.

### 5. No Proxy Creation

Proxy creation must be disabled.

In the generated or partial DbContext, ensure:

```csharp
Configuration.ProxyCreationEnabled = false;
```

The system must not depend on EF dynamic proxy objects.

Returned entity instances should be plain generated POCO instances, not runtime-generated proxy subclasses.

### 6. Context-Side Change Tracking Only

Change tracking should be performed by the `DbContext` / underlying EF6 tracking infrastructure.

Do not implement `INotifyPropertyChanged` on generated entities.

Do not add UI-binding behavior to the entity classes.

Do not generate or require EF proxy-based change tracking.

The expected model is:

```text
POCO entity + attached DbContext = tracked entity
POCO entity without DbContext = detached transport object
```

### 7. POCOs May Be Used as Internal DTO-Like Transport Classes

The generated POCOs are allowed to be used as DTO-like transport objects between internal application layers and, where acceptable, trusted internal process boundaries.

However, keep the design aware of these restrictions:

```text
No lazy loading
No proxies
Avoid uncontrolled bidirectional serialization cycles
Avoid accidental deep graph serialization
Prefer explicit Include/load shapes
Use separate DTOs later if public versioned contracts become necessary
```

Do not introduce duplicate DTOs unless there is an actual contract/versioning/security/serialization reason.

### 8. Navigation Properties

Navigation properties are allowed, for example:

```csharp
public ICollection<Address> Addresses { get; set; }
```

Prefer collection interfaces over EF-specific collection types.

Avoid requiring `virtual` for lazy loading. Since lazy loading and proxy creation are disabled, `virtual` is not required for that purpose.

If the T4 template generates `virtual`, either remove it or document that proxies remain disabled and `virtual` is harmless but unnecessary.

### 9. GUID Primary Keys

The model must support `Guid` primary keys.

Prefer `Guid` / SQL Server `uniqueidentifier` mapping.

Support either:

```text
client-side Guid.NewGuid()
```

or database-generated keys such as:

```sql
NEWID()
NEWSEQUENTIALID()
```

Document which strategy the implementation uses.

For detached object scenarios, client-side GUID generation is often preferable because objects can have stable identities before being attached to a context.

### 10. Detached Graph Handling

Support reasonable attach/detach workflows for POCO graphs with navigation properties.

Implement helper methods or document patterns for:

```text
Attach existing entity
Mark entity Modified
Attach child entities
Handle Added vs Modified children
Avoid duplicate tracked instances with the same key
```

Do not pretend EF6 automatically knows full detached graph state.

For complex graph updates, provide explicit state handling.

Example pattern:

```csharp
context.People.Attach(person);
context.Entry(person).State = EntityState.Modified;

foreach (var address in person.Addresses)
{
    context.Addresses.Attach(address);
    context.Entry(address).State = EntityState.Modified;
}
```

Also call out that deletes, new children, and relationship changes need explicit handling.

### 11. Views and Stored Procedures

The EF6 layer should support mapping database views and stored procedures where appropriate.

Views may be mapped as read-only entities unless update behavior is explicitly configured.

Stored procedures may be imported/mapped using the EDMX designer.

Keep stored-procedure result types clear and do not force them into entity classes unless that is actually correct.

### 12. Testing

Create a testing strategy.

A hybrid unit/integration test setup may use SQLite where practical, but document limitations clearly.

SQLite is acceptable for:

```text
basic CRUD tests
repository/service tests
LINQ shape tests
simple integration smoke tests
```

But do not assume perfect parity with SQL Server for:

```text
stored procedures
SQL Server-specific views
computed columns
constraints
transactions
concurrency behavior
provider-specific SQL
GUID generation defaults
```

Where SQL Server behavior matters, use SQL Server LocalDB, a test database, or a containerized SQL Server instance instead.

### 13. Repository / Data Service Boundary

Create a small data access boundary so application code does not freely manipulate `DbContext` everywhere.

For example:

```csharp
public interface IPersonRepository
{
    Person GetPerson(Guid personId);
    Person GetPersonWithAddresses(Guid personId);
    void SavePerson(Person person);
}
```

Implementation belongs in:

```text
MyApp.Data.EF6
```

The interface can live in either:

```text
MyApp.Application.Abstractions
```

or another neutral project.

Do not put `DbContext` into the entity assembly.

### 14. Example Required Entity Model

Use an example entity shape like this:

```text
Person
    PersonId : Guid
    FirstName : string
    LastName : string
    BirthDate : DateTime
    Addresses : collection of Address

Address
    AddressId : Guid
    PersonId : Guid
    Street : string
    City : string
    PostalCode : string
    Person : navigation property
```

Generate or demonstrate code consistent with this style.

### 15. Serialization Considerations

Because the POCOs may be used for internal transport:

- Avoid EF proxy instances.
- Avoid lazy loading.
- Be careful with bidirectional navigation properties.
- Consider `[JsonIgnore]`, projection models, or separate DTOs only where serialization cycles become a real issue.
- Prefer explicit query shapes for cross-boundary transport.

Example:

```csharp
var person = context.People
    .Include(p => p.Addresses)
    .Single(p => p.PersonId == personId);
```

Do not serialize arbitrary tracked graphs accidentally.

### 16. Do Not Add UI Binding Concerns

Do not implement:

```text
INotifyPropertyChanged
INotifyPropertyChanging
```

on generated POCO entities.

If UI binding requires notifications, create separate ViewModels or binding models.

The entity assembly should remain persistence/domain/transport-oriented, not UI-oriented.

## Expected Deliverables

Produce:

1. A proposed solution/project structure.
2. EF6 package references needed per project.
3. The EDMX / Database First setup steps.
4. The T4 template adjustment strategy.
5. Sample generated-style POCO classes.
6. Sample DbContext configuration disabling lazy loading and proxies.
7. Sample repository/service class.
8. Sample attach/detach graph update pattern.
9. Notes for GUID primary keys.
10. Notes for views and stored procedures.
11. Testing strategy, including SQLite limitations and SQL Server alternatives.
12. Clear warnings about generated files and partial classes.

## Important Constraints

Do not:

- Use EF Core.
- Generate EntityObject-based entities.
- Use EntityCollection<T>.
- Require EntityFramework.dll in the POCO/entity assembly.
- Require lazy loading.
- Require proxy creation.
- Implement INotifyPropertyChanged on entities.
- Manually copy generated POCO files after schema updates.
- Hide detached graph complexity behind vague helper methods.
- Pretend SQLite behaves exactly like SQL Server.

## Style

Prefer pragmatic, maintainable code over architectural ceremony.

Avoid unnecessary DTO duplication unless it solves a real boundary/versioning/security/serialization problem.

The design should be understandable to a senior .NET Framework developer maintaining a legacy-but-still-important application.
