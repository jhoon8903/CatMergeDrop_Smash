using Framework.Poolable.Components;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Framework.Poolable.Diagnosis
{
    /// <summary>
    /// "must be valid unmanaged type" 에러가 또 나면 이 파일로 범인을 특정한다.
    /// 컴파일만 하면 되고 실행할 필요는 없다. 에러가 뜨는 줄의 타입이 해당 Collections 버전에서
    /// managed로 취급되는 타입이다. 문제 해결 후에는 삭제해도 된다.
    /// </summary>
    internal static class UnmanagedCheck
    {
        private static void Assert<T>() where T : unmanaged
        {
            
        }
 
        private static void Run()
        {
            // 컨테이너
            Assert<UnsafeList<Entity>>();
            Assert<UnsafeHashMap<PoolId, Entity>>();
            Assert<UnsafeHashMap<Entity, UnsafeList<Entity>>>();
            Assert<NativeHashMap<PoolId, Entity>>();
            Assert<NativeParallelMultiHashMap<Entity, Entity>>();
 
            // ECS 타입
            Assert<EntityQuery>();
            Assert<Entity>();
 
            // 이 프로젝트 타입
            Assert<PoolId>();
            Assert<PoolDefinition>();
            Assert<PrefabEntityFactory>();
            Assert<EntityPool>();
            Assert<EntityPoolSystem>();
        }
    }
}