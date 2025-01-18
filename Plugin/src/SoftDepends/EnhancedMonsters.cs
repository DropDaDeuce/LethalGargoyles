using System.Runtime.CompilerServices;

namespace LethalGargoyles.src.SoftDepends
{
    public static class EnhancedMonstersCompatibilityLayer
    {
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static void RegisterCustomMonsterEnemyData()
        {
            EnhancedMonsters.Utils.EnemiesDataManager.RegisterEnemy("LethalGargoyle", /*is enemy sellable ?*/ true, /*min value:*/ 180, /*max value:*/ 240, /*mass:*/ 30, /*rank:*/ "A");
            
        }
    }
}