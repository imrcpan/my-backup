# AGENTS.md

Rules for anyone who changes this repository.

## One commit, one thing

- A commit does one thing, and its subject says it without "and" or a list of
  features. If the subject needs a comma or an "and" to be true, split it.
- Keep the kinds of change apart. These are never in one commit:
  - a mechanical change (a reformat, a rename, moving files, removing unused
    code) and a change of behavior;
  - a refactor, a new feature and a bug fix;
  - a change of a rule or a tool and the files that change because of it.
- When a new rule makes old code fail, the order is: first the commit that
  fixes the old code (it passes the checks of the old rule), then the commit
  that adds the rule (it passes its own check). Each commit passes the checks
  by itself.
- A change goes in with its tests and its documentation, in the same commit.
  A test or a note for something committed before is a commit of its own.
- Do not mix in what was not asked for: no drive-by fixes, no unrelated
  formatting. Note it and make a separate commit.
- Stage by path (`git add <paths>`), never `git add -A` or `git add .`, and
  read `git status` and `git diff --cached` before committing. Files that are
  made by tools or machines (build output, logs, scratch) are not committed.

## Commit message

The style is the GNU one: a short subject, a blank line, and a body that tells
what changed and why.

- Subject: `<summary>`.
  - The summary starts with a lower-case letter, has no full stop at the end,
    and says what the commit does, in 72 characters or fewer in all.
  - Say the thing itself ("copy the pixels of a video frame outside the lock"),
    not the activity ("fix bug", "update code", "changes").
- Body: after a blank line, wrapped at 72 characters. Say
  - what was wrong or missing, and what the commit does about it;
  - why it is done this way, when another way looks as likely;
  - what is left out or not known, when that matters (a limit, something not
    measured, something not run on a real device).
    Say only what is true: what was measured, run or read. Do not describe how
    the code works line by line; the diff does that.
- A commit that only repeats its subject in the body may leave the body out.
- DO NOT add Co-Authored-By to commits.
- English only. No emoji, no issue numbers or tickets in the subject, no
  "WIP", "misc" or "fix review comments".

Example:

```
copy the pixels of a video frame outside the lock

VideoOutput held its lock while the screen copied a whole frame, so
libvlc waited to draw the next one. It now has three buffers (the one
being drawn, the last drawn, the one being copied) and only swaps them
under the lock; the copy is outside it.
```

## Language

Everything written in the repository is in English: code, identifiers,
comments, documentation, test names, commit messages and the texts of errors
and logs.

## Design of the interface

Use https://ui.shadcn.com/

## Logging

Exception should not be silently discarded unless there is truly no need
to record it.

For expected logs, you should not log trace stack. For unknown log or
you indeed need trace stack, you should log trace stack. For example,
HTTP errors are known exception and should not log trace stack
unless there is a specific need in that place.

## C# Backend

Use dotnet 11 AOT for backend.

This C# backend is fully AOT-compiled. The following are strictly prohibited:

- Any method that bypasses AOT compilation
- Runtime reflection of any kind
- Namespace-based reflection symbol preservation directives
- Dynamic code generation at runtime

All code must be statically analyzable and AOT-compatible.

Under debug build, you should use JIT for fast dev.

## UI Construction

Use react web for UI. (Use pnpm)

For UI design, use the component library at https://ui.shadcn.com/. Avoid building components from scratch whenever possible; strictly adhere to the library and utilize existing components.

## C# Project File Structure

All `.csproj` files must explicitly include the following properties in the first `<PropertyGroup>`, in this exact order:

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <RootNamespace></RootNamespace>
        <TargetFramework></TargetFramework>
        <LangVersion></LangVersion>
        <OutputType></OutputType>
        <Nullable></Nullable>
        <IsAotCompatible></IsAotCompatible>
        <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
        <!-- other properties below -->
    </PropertyGroup>

    <!-- other elements -->

</Project>
```

Additional properties may follow after `EnableTrimAnalyzer`.

`PublishAot` is set only for the `Release` configuration (`<PropertyGroup Condition="'$(Configuration)' == 'Release'">`). Debug builds and runs
use the JIT, so that development stays fast. Code still has to be AOT-compatible: `IsAotCompatible` and `EnableTrimAnalyzer` report any
violation in every configuration.

## Formatting

Before commit, you MUST run format. (pnpm for react, dotnet format for C#)

## C# Syntax

Do not use `is {}` or `is not {}`; use `some == null` and `some != null` instead. Explicit is better than implicit. You should use `==` to compare equality. If you need to compare reference equality, you should also use `==` to express it clearly instead of using `is`.

Avoid overuse of pattern matching; however, well-applied pattern matching is encouraged. For instance, using it in a construct like `return some switch {}` is explicitly encouraged, as it enhances readability in such scenarios.
