# FBC.DBRepository

## Notes

- For now, only async methods have been implemented. Synchronous methods may be added later.
- The project is prepared for a web application. Support for console applications (using `HostApplicationBuilder`) can be added in the future.
- A practical approach will be chosen for adding pre-predicates to queries, such as checks for user rights and rules.
- Later, the project will be transformed into a high-performance repository by replacing runtime reflection with a source generator. (see: Riok.Mapperly)