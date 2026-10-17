using System.Windows;
using System.Windows.Threading;

namespace SetupAssistant.Helpers;

/// <summary>
/// Присоединяемое свойство для карточки в прокручиваемом списке: когда оно становится true (шаг начал выполняться),
/// карточка прокручивается в видимую область вместе со следующими за ней шагами.
/// </summary>
public static class AutoScroll
{
    /// <summary>Сколько пикселей ниже карточки тоже показать, чтобы был виден «запас» следующих шагов.</summary>
    private const double LookAhead = 140;

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.RegisterAttached(
        "IsActive", typeof(bool), typeof(AutoScroll), new PropertyMetadata(false, OnIsActiveChanged));

    public static bool GetIsActive(DependencyObject element) => (bool)element.GetValue(IsActiveProperty);

    public static void SetIsActive(DependencyObject element, bool value) => element.SetValue(IsActiveProperty, value);

    private static void OnIsActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || d is not FrameworkElement element)
        {
            return;
        }

        // Карточка может быть ещё не измерена (шаг активировался сразу после создания списка) — ждём разметку.
        element.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (element.IsLoaded)
            {
                element.BringIntoView(new Rect(0, 0, element.ActualWidth, element.ActualHeight + LookAhead));
            }
        });
    }
}
