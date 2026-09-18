# StrokeKit

What a pen reports, and what a brush does with it. The drawing core behind
[StrokeFieldGuide](https://github.com/TheSevenPens/StrokeFieldGuide), pulled out so that an
application can take the part that draws without taking the book.

Two projects:

| | holds | needs |
|---|---|---|
| **StrokeKit** | readings, strokes, the trace format, brushes, surfaces, view mathematics, and taking a batch off a pen session | SkiaSharp, WinPenKit |
| **StrokeKit.Avalonia** | the control that puts a surface on screen, the pad that is drawn into, the pen stream that polls a session | Avalonia, WinPenKit.Avalonia, StrokeKit |

The split is the point. **StrokeKit knows nothing about a windowing framework** — it draws into
raster surfaces and can be exercised with no display attached, which is what makes its
behaviour checkable. Avalonia enters at exactly one boundary, in the other project, and that
boundary is small on purpose.

The arrangement is WinPenKit's, deliberately: a core and a framework adapter beside it.

## What is in here, and why it is worth being separate

`Reading` is a pen sample as this family of projects has settled on keeping it — a position, a
raw pressure count, a lean and an azimuth rather than a tilt x and a tilt y, and **two clocks**.
The pen's own timestamp and the moment the application took the packet off the queue are
different things, and a single clock cannot tell "the device stopped sending" from "the device
stamped late". Five explanations for a gap in recorded data died on that before the second
clock existed.

`TraceFormat` is the file a recording is written as: self-describing columns, so a reader takes
what the file declares rather than what this version happens to write. The published corpus at
[StrokeCorpus](https://github.com/TheSevenPens/StrokeCorpus) is in that format.

`Brush`, `Nib`, `Width`, `Flow`, `Spacing` and the engines are immutable values describing a
mark, and the code that lays one down. What each control is for, and what breaks when it is
wrong, is the subject of StrokeFieldGuide rather than of this README — that book is the
documentation for this code, and the checks in it run against these types.

`app/StrokeKit.Lab` is a demonstration: a window you can draw in with a real pen, a surface
view you can zoom and pan, and a set of brushes to try. It came from StrokeFieldGuide, where
it was the book's playground, and it is here because what it demonstrates is this kit rather
than that book. It draws with its own brushes for that reason — the book's named presets are
fixtures its pages measure against, and a kit cannot reference the book that documents it.

## Building

WinPenKit is a submodule, so:

```
git clone --recurse-submodules https://github.com/TheSevenPens/StrokeKit.git
cd StrokeKit
dotnet build StrokeKit.slnx
```

An existing clone that predates the submodule, or one made without `--recurse-submodules`:

```
git submodule update --init --recursive
```

Without it the build fails on a missing project rather than on anything informative, which is
the one rough edge of this arrangement.

**Why a submodule rather than a package.** WinPenKit is pinned here so that a given commit of
this kit builds the same way tomorrow. It is not published as a NuGet package, and neither is
this kit yet — the API is still moving, and a package boundary on an API that is still moving
is a cost with no benefit: every rough edge smoothed becomes a version dance, and nobody should
be depending on the versions that would be published in the meantime. Packages are the right
answer later, once more use has shown where the edges are.

`net10.0-windows`, because WinPenKit targets it.

## Consuming it

The same way: a submodule, and a project reference into it.

```
git submodule add https://github.com/TheSevenPens/StrokeKit.git vendor/StrokeKit
git submodule update --init --recursive
```

```xml
<ProjectReference Include="vendor\StrokeKit\src\StrokeKit\StrokeKit.csproj" />
<ProjectReference Include="vendor\StrokeKit\src\StrokeKit.Avalonia\StrokeKit.Avalonia.csproj" />
```

WinPenKit comes with it, nested, which is why the paths inside this repository are relative to
the repository and not to whatever sits beside it.

## Tests

`dotnet test StrokeKit.slnx` runs two suites. `StrokeKit.Tests` is arithmetic and files — the
trace format's column handling, the batch clock, the contracts a live stroke is built under —
and opens no window. `StrokeKit.Lab.Tests` does: it runs real windows on Avalonia's headless
platform and compares captured frames against the surfaces they came from.

The two are on different xunit versions, v2 and v3, because `Avalonia.Headless.XUnit` depends
on v3. One `dotnet test` over the solution runs both.

The lab also checks itself as a built program rather than from a test: `StrokeKit.Lab.exe
--selftest` prints a line per check and exits non-zero if any fails. CI runs it.

Plenty here is still covered from the consumer's side by StrokeFieldGuide's suite, which is
where the brush engines and the corpus checks are exercised. That book is the documentation
for this code.

## History

The commits here are the real ones. The files were extracted with their history intact, so
`git log --follow` on any of them reaches back to where it was first written inside
StrokeFieldGuide, which is where all of this was measured and argued out.
