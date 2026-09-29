using System.Threading;
using Cysharp.Threading.Tasks;
using Framework.Scene;

namespace Projects.Scripts.Screen.Loading
{
    public class LoadingScreen : LoadingView
    {
        public override UniTask ShowAsync(CancellationToken cancellationToken)
        {
            return UniTask.CompletedTask;
        }

        public override void SetProgress(float progress)
        {
            return;
        }

        public override UniTask HideAsync(CancellationToken cancellationToken)
        {
            return UniTask.CompletedTask;
        }
    }
}