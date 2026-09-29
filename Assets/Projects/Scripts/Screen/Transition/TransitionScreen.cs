using System.Threading;
using Cysharp.Threading.Tasks;
using Framework.Scene;

namespace Projects.Scripts.Screen.Transition
{
    public class TransitionScreen : TransitionView
    {
        public override UniTask ShowAsync(CancellationToken cancellationToken)
        {
            return UniTask.CompletedTask;
        }

        public override UniTask HideAsync(CancellationToken cancellationToken)
        {
            return UniTask.CompletedTask;
        }
    }
}