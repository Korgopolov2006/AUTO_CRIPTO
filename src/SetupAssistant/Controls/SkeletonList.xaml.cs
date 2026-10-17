using System.Windows;
using System.Windows.Controls;

namespace SetupAssistant.Controls;

/// <summary>Заглушка-«скелетон» на время загрузки списка: пульсирующие серые строки вместо пустого экрана.</summary>
public partial class SkeletonList : UserControl
{
    public static readonly DependencyProperty RowsProperty =
        DependencyProperty.Register(nameof(Rows), typeof(int), typeof(SkeletonList), new PropertyMetadata(6, OnRowsChanged));

    public SkeletonList()
    {
        InitializeComponent();
        Rows = 6;
        OnRowsChanged(this, new DependencyPropertyChangedEventArgs(RowsProperty, 0, 6));
    }

    public int Rows
    {
        get => (int)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    private static void OnRowsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SkeletonList list)
        {
            list.RowsHost.ItemsSource = Enumerable.Range(0, Math.Max(0, list.Rows)).ToList();
        }
    }
}
