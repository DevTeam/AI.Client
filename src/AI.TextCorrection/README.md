# Text correction

Local keyboard layout analysis for the chat composer. `TextCorrectionComposition`
provides `ITextCorrectionAnalyzer` and `IKeyboardLayouts` through Pure.DI.
Registrations live in `Configuration.TextCorrectionComposition` in `Composition.cs`.
Build and Web link that setup and reuse it with `DependsOn`; the public standalone
composition adds only roots for library consumers, tests and benchmarks.
All handwritten services and composition methods are instance members.

The analyzer considers the enabled layouts, preserves known words in any of them,
and accepts a unique dictionary-backed conversion. Short words, including single
letters, also require a nearby longer correction or known word in the target
layout within the same phrase. Three known words of three letters each can
also support one another: `рщц фку нщг?` becomes `how are you?`.
The rules operate in both directions. Context does not cross sentence boundaries,
line breaks, protected code, or actual words of another language. Unknown lowercase
tokens may be converted literally if their dictionary-derived letter-triple score
is at least 0.5 and exceeds the original's score in every other enabled language
by at least 0.25. At least two nearby known target-language words must support
the conversion, including one of five or more letters. This
restores the layout but preserves any typo in the original keystrokes:
`gjqltim cj vyj d rbyj` becomes `пойдешь со мно в кино`; `vyjq` means `мной`.
Speculative conversions never supply context for other corrections. The score ranks
heuristic evidence; it is not a calibrated probability. `HunspellWordLexicon`
loads embedded English (US) and Russian `.dic`/`.aff` dictionaries once per
language, including inflection rules. `WordLexicon` supplements them with the
application's conversational and technical vocabulary. Unknown
words outside that strongly supported phrase case and ambiguous alternatives are
preserved. Other layouts can be added to
`IKeyboardLayouts` together with an `IWordLexicon` vocabulary.

`ITextDictionaries` catalogs available `ITextDictionaryResource` instances.
The embedded provider discovers `.dic`/`.aff` pairs by resource name; the analyzer
does not branch on language IDs. `IWordPlausibility` reads a build-time index of
letter triples from each UTF-8 dictionary, without separate vowel rules for individual languages.
The score is a conservative heuristic, not a calibrated probability.

`ISupportedCorrectionLayouts` automatically discovers catalog layouts whose
`LanguageId` has an available dictionary. The application's general Settings panel
lists correction languages inside a collapsed disclosure, using
the same design as Update options. No languages are
selected by default. Fewer than two selected languages disables correction and
dictionary preparation. The analyzer identifies the
correction direction from the text. Language preferences are stored on the device.
`ITextCorrectionPreparation` prepares those languages in the background when the
page initializes. Hunspell loading
uses asynchronous, bounded stream reads with a browser event-loop handoff after
roughly four milliseconds of parsing work. The bundled letter models load compact
binary indexes without rescanning dictionaries or introducing per-batch delays.
This is cooperative initialization for single-threaded WASM, not a separate worker
thread. It does not depend on `Task.Run`. Analysis awaits preparation if its selected
languages are still loading; the first completed word is not silently skipped.
Repeated preparation calls reuse the same loaded singleton dictionary/model data.

`KeyboardLayout.LanguageId` separates the keyboard arrangement from its language:
multiple variants can share one dictionary and one letter model.

To add a language, supply its keyboard map in `IKeyboardLayouts` and a matching
UTF-8 Hunspell pair in `Dictionaries/<language-id>/index.dic` and `index.aff`, with
its license. Run the manual preparation target to generate its letter model;
the composer lists layouts from the injected catalog. An alternative
`ITextDictionaries` implementation can supply other dictionary sources.
Providers implementing `IPreparedTextDictionaryResource` can supply compiled indexes;
other providers retain cooperative model preparation from their word lists.
This positional conversion assumes maps of individual characters; IME composition
and multi-keystroke/dead-key mappings require a specialized `ILayoutConverter`.

The web composer checks completed prefixes after whitespace, a line break or
punctuation that cannot become a letter in any supported layout. `IWordBoundaries`
derives ambiguous separators from the maps, without language-specific lists.
Pauses and ordinary letters never complete the last token. Punctuation such as
dots and commas can represent letters in a different keyboard layout.
Appending another word does not cancel analysis of a completed prefix. During
typing, pastes and IME composition are excluded. Recent edits are applied only
while the completed prefix is unchanged and the caret and focus remain valid.
All submit modes, including Enter and the send button, await dictionary preparation
and analyze the entire message including its final word. Editing the draft while
this check is pending aborts that submission. Ctrl+Z immediately after a
correction restores the original and suppresses those words until the composer
is reattached. The operating system's active keyboard layout is unchanged.

Correction runs automatically without additional controls in the composer.
The bundled catalog and dictionaries currently provide English (US), Russian,
French (classic AZERTY) and Spanish (Spain). French letters printed on the number
row can be recovered from mistyped digits. Dead-key composition and AltGr sequences
still require a specialized converter beyond the single-character maps.

Resource generation lives in `build/Targets/PrepareTextCorrectionTarget.cs`, behind
`IPrepareTextCorrectionTarget` and the existing Pure.DI build composition.
Run it manually after importing or changing dictionaries (or the index format),
using the Rider configuration **Prepare Text Correction** or the command below.
It writes `Dictionaries/<language>/index.trigrams` next to the source dictionaries.
Commit these generated files with the dictionary changes. Normal builds and
publishing embed the committed resources without running the generator or
depending on the `build` project. Repeated manual runs preserve unchanged files.
The dictionary tests check that indexes match their sources.

```powershell
dotnet run --project build -c Release -- prepare-text-correction
```

The `TCG1` format contains a version tag, SHA-256 fingerprint of the source `.dic`,
count and sorted unique 64-bit encodings of three UTF-16 letters. Values are
normalized with invariant lowercase and stored little endian. The same instance
codec lives in the library and is reused by the build tool through the shared composition.
Runtime scoring binary-searches
these arrays through spans without allocating substring keys. Runtime validation
checks the format and ordering; the source fingerprint records provenance and does
not require rehashing the full word list on startup.

Hunspell still needs `.aff` for inflections and dictionary rules; the compiled
trigram index replaces only the plausibility-model preparation. Protected code,
URLs, command prefixes and word boundaries are scanned linearly without regular
expressions. The converter caches character lookup tables for each layout pair,
and separator policies are cached for each selected layout set.

Performance scenarios and initial reports live in
`benchmarks/AI.TextCorrection.Benchmarks`. Measure on the same runtime/machine/job;
desktop .NET timings do not replace browser/WASM responsiveness measurements.

Run checks from the repository root:

```powershell
dotnet test --project tests/AI.TextCorrection.Tests/AI.TextCorrection.Tests.csproj
node --test tests/AI.TextCorrection.Tests/JavaScript/textCorrection.test.mjs
```
