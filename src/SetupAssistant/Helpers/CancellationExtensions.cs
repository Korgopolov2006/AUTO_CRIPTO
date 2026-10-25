namespace SetupAssistant.Helpers;

public static class CancellationExtensions
{
    /// <summary>
    /// Отменяет из потока пула. <see cref="CancellationTokenSource.Cancel()"/> выполняет подписчиков синхронно, а продолжения после
    /// <c>await</c> могут пойти прямо в вызывающем потоке: из обработчика кнопки «Отмена» это значит «внутри окна приложения»,
    /// и всё, что делает отмена (остановка установщика, ожидание), подвешивает интерфейс.
    /// </summary>
    public static void CancelInBackground(this CancellationTokenSource? source)
    {
        if (source is null)
        {
            return;
        }

        ThreadPool.QueueUserWorkItem(static state =>
        {
            try
            {
                ((CancellationTokenSource)state!).Cancel();
            }
            catch (Exception)
            {
                // Источник уже освобождён или подписчик бросил исключение: на пуле оно закрыло бы приложение, а отменять больше нечего.
            }
        }, source);
    }
}
