# Third-Party Notices

ViewGrid uses or includes derivatives of the following third-party assets and libraries.
This file lists each item with its license and source.

---

## Material Icons (Path Geometry)

ViewGrid embeds path geometry data adapted from a small subset of Google's Material Icons
(Filled style: undo, redo, expand_more) in `src/ViewGrid.Presentation/App.axaml` as
`StreamGeometry` resources. The geometries are used for the Undo / Redo / dropdown buttons
in the editor toolbar.

**Source**: <https://fonts.google.com/icons>

**License**: Apache License, Version 2.0
<https://www.apache.org/licenses/LICENSE-2.0>

```
Copyright 2014–present Google LLC

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
```

---

## NuGet Package Dependencies

The following NuGet packages are referenced by the project. Their licenses apply to the
shipped binaries when the application is published. See each package's NuGet listing for
the canonical license text.

| Package | License |
|---|---|
| Avalonia (12.x) | MIT |
| Avalonia.Themes.Fluent (12.x) | MIT |
| CommunityToolkit.Mvvm (8.x) | MIT |
| Microsoft.EntityFrameworkCore.Sqlite (10.x) | MIT |
| Microsoft.Extensions.* (10.x) | MIT |
| Serilog (4.x) | Apache-2.0 |
| Serilog.Sinks.File / Serilog.Sinks.Console | Apache-2.0 |
| SkiaSharp (3.x) | MIT |
| ErrorOr (2.x) | MIT |
| FluentValidation (11.x) | Apache-2.0 |
| FluentAssertions (6.12.2, test only) | Apache-2.0 |
| xUnit (2.x, test only) | Apache-2.0 |
| NSubstitute (test only) | BSD-3-Clause |

If you redistribute the published binaries, ensure that the corresponding license texts
of the included packages are bundled per each package's terms. Most MIT/Apache licensed
packages require attribution and the unmodified license notice in distributed copies.
