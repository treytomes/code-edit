# Spec: File Open / Save / Save As

## Status
Approved

## Overview
Wire up the File menu's Open, Save, and Save As items so users can open files from disk, save changes back to the same file, and save to a new path. Opening a file updates the syntax provider and resets editor state. Saving clears the dirty flag and updates the title/status bar.

## Scope

**In scope**
- File > Open — shows `OpenDialog`, opens chosen file, replaces the current buffer
- File > Save — saves current buffer to its existing path; shows Save As dialog if buffer has no path (i.e. it is an `EmptyBuffer` or was never saved)
- File > Save As — always shows `SaveDialog`, saves to chosen path, re-opens the result as a `LazyFileBuffer` and sets it as the active buffer
- Status bar and window title reflect the new path and dirty state after each operation
- Error messages shown in the status bar for I/O failures (file not found, permission denied, disk full, etc.)
- Unsaved-changes prompt on Open when the buffer `IsDirty` — "Unsaved changes. Open anyway? (Y/N)" via a simple `MessageBox`

**Out of scope**
- Multiple open buffers / tabs
- Recent files list
- Encoding selection
- Auto-save

## Design

### `IFileService` (already implemented)
```
ITextBuffer Open(string path)
void Save(ITextBuffer buffer, string path)      // save existing LazyFileBuffer to same or new path
void SaveNew(ITextBuffer buffer, string path)   // save any buffer to a new path (EmptyBuffer → LazyFileBuffer)
```

`FileService.Save` and `SaveNew` both call `SaveCore`, which requires a `LazyFileBuffer`.  
`SaveNew` must handle `EmptyBuffer` → needs to either write to disk directly or convert. See §EmptyBuffer save below.

### EmptyBuffer save
`FileService.SaveNew` currently throws if the buffer is not a `LazyFileBuffer`. For Save As from an `EmptyBuffer`, we need to write its content to disk. Two options:

**Chosen approach**: extend `FileService.SaveNew` to handle any `ITextBuffer` by writing lines directly to a temp-then-replace pattern, then re-opening the file as a `LazyFileBuffer` and returning it. The caller is responsible for calling `eventBus.SetBuffer` and `editorView.SetBuffer` with the returned buffer.

Update `IFileService`:
```csharp
// Returns the new LazyFileBuffer after saving (caller must re-set it as the active buffer)
ITextBuffer SaveNew(ITextBuffer buffer, string path);
```

`FileService.SaveNew` implementation:
```csharp
public ITextBuffer SaveNew(ITextBuffer buffer, string path)
{
    if (buffer is LazyFileBuffer lazy)
    {
        lazy.SaveTo(path);          // existing path
    }
    else
    {
        WriteLinesTo(buffer, path); // new helper for EmptyBuffer / any ITextBuffer
    }
    return Open(path);              // re-open to get a clean LazyFileBuffer
}

private static void WriteLinesTo(ITextBuffer buffer, string path)
{
    var tempPath = path + ".tmp";
    var utf8NoBom = new UTF8Encoding(false);
    using (var w = new StreamWriter(tempPath, false, utf8NoBom))
    {
        for (var i = 0; i < buffer.LineCount; i++)
        {
            if (i > 0) w.Write('\n');
            w.Write(buffer.GetLine(i));
        }
    }
    File.Replace(tempPath, path, null);
}
```

Similarly, update `FileService.Save` to delegate to `SaveNew` when the path differs from the buffer's current path (Save As reuses this path).

### `AppBootstrap` wiring

The open/save operations need access to `eventBus`, `editorView`, `statusBar`, `fileService`, and `app` (for running dialogs modally). These are all available at bootstrap time as locals. Wire the menu actions as local lambdas.

#### Open
```
1. If buffer.IsDirty: show MessageBox "Unsaved changes — open anyway?" → cancel if No
2. Show OpenDialog (MustExist = true, OpenMode = File)
3. Run dialog modally via app.Run(dialog)
4. If dialog.Canceled or no path selected: return
5. path = dialog.FilePaths[0].ToString()
6. newBuffer = fileService.Open(path)          // throws FileServiceException on error
7. eventBus.SetBuffer((IMutableTextBuffer)newBuffer)
8. editorView.SetBuffer((IMutableTextBuffer)newBuffer)
9. statusBar sets message on error; clears on success (OnBufferChanged handles it)
```

#### Save
```
1. If buffer has no FilePath (EmptyBuffer or unsaved): delegate to Save As flow
2. Otherwise: fileService.Save(buffer, buffer.FilePath)
3. SetNeedsDraw on editorView (dirty flag cleared, status bar refreshes via EventBus)
4. Show status message "Saved." briefly? — No: the status bar already shows dirty state.
   On error: statusBar.SetMessage(ex.Message)
```

The status bar already redraws on `EventExecuted`; but Save doesn't publish an event. We need a lightweight way to trigger a status bar refresh. **Approach**: add `IEventBus.NotifyBufferChanged()` — fires `EventExecuted` with a no-op sentinel event — or simply call `statusBar.SetNeedsDraw()` directly from the save lambda. The latter is simpler and avoids polluting the undo stack.

**Chosen**: call `statusBar.SetNeedsDraw()` directly after save, and `editorView.SetNeedsDraw()` to refresh the title-bar dirty indicator.

#### Save As
```
1. Show SaveDialog
2. Run dialog modally
3. If dialog.Canceled or FileName is null: return
4. path = dialog.FileName.ToString()
5. newBuffer = fileService.SaveNew(buffer, path)   // writes + re-opens
6. eventBus.SetBuffer((IMutableTextBuffer)newBuffer)
7. editorView.SetBuffer((IMutableTextBuffer)newBuffer)
   (SetBuffer resets scroll, re-detects syntax, triggers redraw)
8. On error: statusBar.SetMessage(ex.Message)
```

Re-opening via `Open` after Save As ensures the buffer is a proper `LazyFileBuffer` backed by the saved file, and the syntax provider is re-detected for the new extension.

### Terminal.Gui v2 dialog usage

```csharp
// Open
var dlg = new OpenDialog { MustExist = true };
app.Run(dlg);
if (!dlg.Canceled && dlg.FilePaths.Count > 0)
    path = dlg.FilePaths[0].ToString();

// Save As
var dlg = new SaveDialog();
app.Run(dlg);
if (dlg.FileName is not null)
    path = dlg.FileName.ToString();

// Unsaved-changes confirmation
var result = MessageBox.Query("Unsaved Changes", "Open anyway?", "Yes", "No");
if (result != 0) return;  // 0 = first button = Yes
```

### Error handling
Catch `FileServiceException` in each menu lambda and call `statusBar.SetMessage(ex.Message)`. Do not rethrow — the user can try again.

## UI / UX

### Menu items
```
_File
  _Open       Ctrl+O
  _Save       Ctrl+S
  _Save As…
  ─────────
  _Quit
```

### Status bar after save
The dirty indicator (`*`) disappears from the status bar immediately after a successful save because `IsDirty` returns false on the buffer once `RebuildAfterSave` runs.

### Unsaved-changes dialog
```
┌─ Unsaved Changes ────────────────┐
│ You have unsaved changes.        │
│ Open a new file anyway?          │
│                                  │
│    [ Yes ]       [ No ]          │
└──────────────────────────────────┘
```

## Open Questions
_(none)_
