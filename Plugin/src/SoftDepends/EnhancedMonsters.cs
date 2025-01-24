using System.Runtime.CompilerServices;
using System.Numerics;

namespace LethalGargoyles.src.SoftDepends
{
    public static class EnhancedMonstersCompatibilityLayer
    {
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static void RegisterCustomMonsterEnemyData()
        {
            EnhancedMonsters.Utils.EnemyData.EnemyMetadata enemyMetadata = new()
            {
                MeshRotation = new Vector3(0, 90, 0),
                AnimateOnDeath = false,
                MeshOffset = new Vector3(0, 0, 0),
            };

            EnhancedMonsters.Utils.EnemiesDataManager.RegisterEnemy("LethalGargoyle", /*is enemy sellable ?*/ true, /*min value:*/ 70, /*max value:*/ 90, /*mass:*/ 30, /*rank:*/ "B", enemyMetadata);
        }
    }
}