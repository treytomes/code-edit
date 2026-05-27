namespace CodeEdit.Infrastructure.Buffer;

internal record struct LogicalLine(
    int PhysicalIndex,  // index into _physicalOffsets; -1 for inserted lines
    bool IsEdited,
    string EditedText   // meaningful only when IsEdited is true
);
