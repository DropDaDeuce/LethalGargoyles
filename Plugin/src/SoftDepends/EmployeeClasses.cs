using GameNetcodeStuff;
using System;
using UnityEngine;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Diagnostics;

namespace LethalGargoyles.src.SoftDepends
{
    internal class EmployeeClassesClass
    {
        [Conditional("DEBUG")]
        static void LogIfDebugBuild(string text)
        {
            Plugin.Logger.LogInfo(text);
        }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static string? GetPlayerClass(PlayerControllerB player)
        {
            if (player == null)
                return null;
            try
            {
                // Get the RoleManager component using the RoleManager type
                // Get the EmployeeClasses plugin instance
                if (BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("Jade.EmployeeClasses", out BepInEx.PluginInfo employeeClassesPlugin))
                {
                    // Get the RoleManager type from the plugin instance
                    Type RoleManager = employeeClassesPlugin.Instance.GetType().Assembly.GetType("EmployeeClasses.Roles.RoleManager");

                    if (RoleManager != null)
                    {
                        Component roleManagerComponent = player.GetComponent(RoleManager);
                        if (roleManagerComponent != null)
                        {
                            // Get the selectedRole property (assuming it's public)
                            FieldInfo selectedRoleField = RoleManager.GetField("selectedRole", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                            if (selectedRoleField != null)
                            {
                                // Get the value of the selectedRole field
                                string? playerClass = (string?)selectedRoleField.GetValue(roleManagerComponent);
                                if (playerClass == null)
                                {
                                    LogIfDebugBuild("Players class is null");
                                    return null;
                                }
                                else
                                {
                                    return playerClass;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Log the exception and return null
                Plugin.Logger.LogError($"Error getting player class from EmployeeClasses: {ex}");
                return null;
            }

            return null;
        }
    }
}
