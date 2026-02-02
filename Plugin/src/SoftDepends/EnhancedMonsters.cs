using System.Runtime.CompilerServices;
using UnityEngine; // <-- use UnityEngine.Vector3 (simpler)

namespace LethalGargoyles.src.SoftDepends
{
    public static class EnhancedMonstersCompatibilityLayer
    {
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static void RegisterCustomMonsterEnemyData()
        {
            var enemyMetadata = new EnhancedMonsters.Utils.EnemyData.EnemyMetadata
            {
                MeshOffset = new Vector3(0f, 0f, 0f),

                // Replaces old MeshRotation:
                FloorRotation = new Vector3(0f, 90f, 0f),
                HandRotation = new Vector3(0f, 90f, 0f),

                AnimateOnDeath = false,
            };

            EnhancedMonsters.Utils.EnemiesDataManager.RegisterEnemy(
                "LethalGargoyle",
                true,   // pickupable / sellable
                70,     // min value
                90,     // max value
                30f,    // mass
                "B",
                enemyMetadata
            );
        }
    }
}