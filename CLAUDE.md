# TradeRouter.Net

## Git and GitHub attribution

- Commits, pushes and pull requests are authored only as the GitHub handle `skuirrels`. Do not use any other name.
- Never add `Co-Authored-By` trailers, "Generated with Claude Code" footers, or any other AI attribution to commit messages, pull request titles or descriptions, issue comments or release notes.
- Do not push, open pull requests or create releases unless explicitly asked.

## Build

- Run `dotnet build`, `dotnet test` and `dotnet pack` with `-m:1 -nr:false`. Multi-node MSBuild fails in the sandbox with MSB4166.
- Solution file is `TradeRouter.Net.slnx`. There is no `.sln`.

## Dependencies

- No packages with a commercial licence. FluentAssertions stays on the 7.x line.
