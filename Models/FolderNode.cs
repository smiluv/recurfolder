using System.Collections.ObjectModel;
using System.ComponentModel;

namespace RecurFolder.Models;

public enum FolderKind
{
    Computer,
    Drive,
    Folder,
    Placeholder
}

public sealed class FolderNode : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;

    public FolderNode(string name, string fullPath, FolderKind kind)
    {
        Name = name;
        FullPath = fullPath;
        Kind = kind;
    }

    public string Name { get; }

    public string FullPath { get; }

    public FolderKind Kind { get; }

    public ObservableCollection<FolderNode> Children { get; } = [];

    public bool ChildrenLoaded { get; set; }

    public string Tip => string.IsNullOrEmpty(FullPath) ? Name : FullPath;

    public string Glyph => Kind switch
    {
        FolderKind.Computer => "\uE977",
        FolderKind.Drive => "\uEDA2",
        FolderKind.Folder => "\uE8B7",
        _ => ""
    };

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
                return;

            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            if (value)
                Expanded?.Invoke(this);
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            if (value)
                Selected?.Invoke(this);
        }
    }

    public event Action<FolderNode>? Expanded;

    public event Action<FolderNode>? Selected;

    public event PropertyChangedEventHandler? PropertyChanged;
}
