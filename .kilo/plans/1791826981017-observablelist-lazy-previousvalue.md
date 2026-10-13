# ObservableList: lazy, delta-based `PreviousValue`

## Problem
In 1.0.17, `ObservableList<T>.CapturePrevious()` (`Runtime/ObservableList.cs:172`) calls `_items.ToArray()` on every change, even with no subscribers. 1.0.16 avoided that by only copying when `OnChanged` had handlers. Tests added in 1.0.17 rely on the new behavior: `PreviousValue` must be correct without subscribers (`Tests/Editor/ObservablePrimitivesTests.cs:194`, `Tests/Editor/Pipeline/MmvcSelfTest.cs:116-132`). Simply putting the old subscriber check back would break those tests.

## Decision
Keep the 1.0.17 behavior: `PreviousValue` is always correct, with or without subscribers. Stop copying the list on each change. Instead, record a small description of the latest change (a delta) and rebuild the previous list only when someone reads `PreviousValue`.

## Design (all in `Runtime/ObservableList.cs`)
Add `[NonSerialized]` fields:
- `_deltaKind` enum `PrevDelta { Empty, Inserted, Removed, Replaced, Unchanged, Snapshot }`
- `_deltaIndex` (int), `_deltaCount` (int), `_deltaItem` (T), `_deltaSnapshot` (T[])
- `_previousCache` (IReadOnlyList<T>): the rebuilt list, cleared on every change
- `_itemsAtDelta` (List<T>) and `_countAtDelta` (int): used to detect changes made outside this class

What each change records (no allocation unless marked):
| Change | Delta | How the previous list is rebuilt |
|---|---|---|
| `Add(item)` | Inserted(index=oldCount, count=1) | current minus `[index, index+count)` |
| `Insert(i, item)` | Inserted(i, 1) | same |
| `AddRange` | Inserted(oldCount, newCount-oldCount) | same (works for enumerables of unknown length) |
| `RemoveAt(i)` / `Remove(item)` | Removed(i, removedItem) | current with item inserted at i |
| indexer set `[i]=v` | Replaced(i, oldItem) | current with `[i]=oldItem` |
| `ForceNotify()` | Unchanged | copy of current |
| `Clear()` | Snapshot(`_items.ToArray()`): **still allocates** | snapshot |
| `Value = list` | Snapshot(old items, or empty if count is 0): **still allocates** | snapshot |
| editor `OnValidate` | Snapshot(already-allocated `previous`) | snapshot |

Getter:
```csharp
public IReadOnlyList<T> PreviousValue => _previousCache ??= MaterializePrevious();
```
- `MaterializePrevious` always returns a new `T[]` (or `Array.Empty<T>()`), so a reference a handler keeps stays fixed after later changes. That matches 1.0.17.
- Replace `CapturePrevious()` with `RecordInserted/RecordRemoved/RecordReplaced/RecordUnchanged/RecordSnapshot` helpers. Each helper sets `_previousCache = null`, stores `_itemsAtDelta = _items`, and sets `_countAtDelta` to the count after the change. Set `_countAtDelta` after the change happens, or compute it ahead of time.
- Initial state is `Empty`, which rebuilds to `Array.Empty<T>()`.
- For `Clear()` and the `Value` setter, rebuild from the snapshot only, so the outside-change check doesn't apply.
- After a `Removed` or `Replaced` change, set `_deltaItem = default` once the cache is built. This avoids keeping the removed item alive longer than needed. Optional but cheap.

Guard against changes made outside this class (for example `list.Value.Add(x)` without `ForceNotify`, or Unity replacing `_items` during deserialization):
- If `!ReferenceEquals(_items, _itemsAtDelta) || _items.Count != _countAtDelta` for an Inserted, Removed, Replaced, or Unchanged delta, the delta can't be applied. In that case:
  - log a single `Debug.LogWarning` in the editor/development build, and
  - return a copy of the current list.
- This check can't catch in-place element writes made through `Value[i]`. Document that limit in an XML doc comment on `PreviousValue`.

Re-entrancy: if a handler changes the list during a notification, the new delta replaces the old one. That is the same as 1.0.17, where `CapturePrevious` was overwritten.

## Tasks
1. Put the delta fields, record helpers, `MaterializePrevious`, and the outside-change check into `ObservableList.cs`. Then remove `CapturePrevious` and the `private set` on `PreviousValue`. Update `OnValidate` (`:157`) to use `RecordSnapshot(previous)`.
2. Keep all existing tests unchanged. Add tests to `ObservablePrimitivesTests.cs`:
   - Without subscribers, `PreviousValue` is correct after `Insert` (middle), `RemoveAt` (first, middle, last), `AddRange` (array and lazy `IEnumerable`), `ForceNotify`, `Value` replacement, and an indexer set on the last element.
   - A `PreviousValue` reference kept from one change stays the same after a later change.
   - Reading `PreviousValue` twice without a change in between returns the same instance.
   - Allocation regression: give the list spare capacity first (`list.Value.Capacity = 64`), make one change to create `_onChanged`, then `Assert.That(() => list.Add(x), Is.Not.AllocatingGCMemory())`. Repeat for `Insert`, `RemoveAt`, and the indexer set. This needs `UnityEngine.TestTools.Constraints`. Check that the test asmdef references `UnityEngine.TestRunner`, and add the reference if it's missing.
   - Outside change: `list.Add(a); list.Value.Add(b);`, then read `PreviousValue`. It should not throw and should return the current contents. Use `LogAssert.Expect(LogType.Warning, ...)`.
3. Update the 1.0.17 entry in `CHANGELOG.md`, lines 10-11. Replace "now unconditionally snapshots" with: PreviousValue is always available, rebuilt on read from a recorded mutation delta. There is no per-mutation allocation, except `Clear` and `Value` replacement. This assumes 1.0.17 hasn't been published: `package.json` is 1.0.17, the latest git tag is 1.0.13, and the entry is dated today. If it has been published, add a 1.0.18 entry and bump `package.json` instead.

## Validation
- Run EditMode tests (Unity Test Runner or `unity-cli`): all of `ObservablePrimitivesTests`, plus `MmvcSelfTest` if Pipeline is installed.
- The new `Is.Not.AllocatingGCMemory` tests are the regression guard for the warning in this task.

## Out of scope / accepted trade-offs
- `Clear()` and `Value` replacement still allocate once per call. Clearing in place keeps `Value` returning the same list object, which outside code may rely on.
- No change to `ObservableVariable<T>`. It stores `PreviousValue` directly and doesn't allocate.
