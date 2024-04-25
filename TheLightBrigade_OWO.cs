using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MelonLoader;
using HarmonyLib;
using MyOwoVest;
using LB;
using UnityEngine;
using Unity.Mathematics;
using System.Diagnostics;

[assembly: MelonInfo(typeof(TheLightBrigade_OWO.TheLightBrigade_OWO), "TheLightBrigade_OWO", "1.0.0", "Florian Fahrenberger")]
[assembly: MelonGame("Funktronic Labs", "The Light Brigade")]


namespace TheLightBrigade_OWO
{
    public class TheLightBrigade_OWO : MelonMod
    {
        public static TactsuitVR tactsuitVr;
        public static bool rightFoot = true;
        public static bool rightHanded = true;
        public static double lastFootStep = 0.0;
        public static Stopwatch footStepTimer = new Stopwatch();
        public static Unity.Mathematics.Random myRandom = new Unity.Mathematics.Random();

        public override void OnInitializeMelon()
        {
            tactsuitVr = new TactsuitVR();
        }

        #region Weapons

        [HarmonyPatch(typeof(Weapon_Rifle), "TryFire", new Type[] { })]
        public class bhaptics_RifleFire
        {
            [HarmonyPrefix]
            public static void Prefix(Weapon_Rifle __instance, bool ___boltOpenState, float ___nextShot, bool ___hammerOpenState)
            {
                if (___boltOpenState) { return; }
                if (__instance.TypeOfWeapon != WeaponType.Pistol)
                    if ((UnityEngine.Object)__instance.nodeHammer != (UnityEngine.Object)null && ___hammerOpenState) { return; }
                if ((BaseConfig)__instance.chamber == (BaseConfig)null || __instance.chamberSpent) { return; }
                bool isRight = __instance.grabTrigger.gripController.IsRightController();
                bool twoHanded = false;
                if ((UnityEngine.Object)__instance.grabBarrel != (UnityEngine.Object)null)
                    if ((UnityEngine.Object)__instance.grabBarrel.gripController != (UnityEngine.Object)null)
                        twoHanded = true;
                //twoHanded = __instance.grabTrigger.alternateGrabAlso;
                //twoHanded = (__instance.grabBarrel != null);
                tactsuitVr.GunRecoil(isRight, 1.0f, twoHanded);
            }
        }

        [HarmonyPatch(typeof(Weapon_Wand), "OnHeldTriggerRelease", new Type[] { typeof(XRController) })]
        public class bhaptics_CastSpell
        {
            [HarmonyPostfix]
            public static void Postfix(Weapon_Wand __instance, XRController controller)
            {
                bool isRight = controller.IsRightController();
                tactsuitVr.CastSpell(isRight);
            }
        }

        [HarmonyPatch(typeof(Weapon_Bow), "OnGrabStopString", new Type[] { typeof(XRController) })]
        public class bhaptics_ShootBow
        {
            [HarmonyPostfix]
            public static void Postfix(Weapon_Bow __instance, XRController controller)
            {
                bool isRight = controller.IsRightController();
                tactsuitVr.ShootBow(!isRight);
            }
        }

        [HarmonyPatch(typeof(Weapon_Blunt), "OnCollisionEnter", new Type[] { typeof(Collision) })]
        public class bhaptics_SwordCollide
        {
            [HarmonyPostfix]
            public static void Postfix(Weapon_Blunt __instance, Collision collision)
            {
                float speed = collision.relativeVelocity.magnitude;
                tactsuitVr.SwordRecoil(true, speed / 10.0f);
            }
        }

        [HarmonyPatch(typeof(InventorySlot), "OnStoreItemFX", new Type[] { })]
        public class bhaptics_StoreInventory
        {
            [HarmonyPostfix]
            public static void Postfix(InventorySlot __instance)
            {
                if (__instance.inventorySlotType == InventorySlotType.Rifle) tactsuitVr.PlayBackFeedback("RifleStore");
                if (__instance.inventorySlotType == InventorySlotType.Ammo)
                {
                    if (rightHanded) tactsuitVr.PlayBackFeedback("StoreAmmo_L");
                    else tactsuitVr.PlayBackFeedback("StoreAmmo_R");
                }
            }
        }

        [HarmonyPatch(typeof(InventorySlot), "OnUnstoreItemFX", new Type[] { })]
        public class bhaptics_ReceiveInventory
        {
            [HarmonyPostfix]
            public static void Postfix(InventorySlot __instance)
            {
                if (__instance.inventorySlotType == InventorySlotType.Rifle) tactsuitVr.PlayBackFeedback("RifleReceive");
                if (__instance.inventorySlotType == InventorySlotType.Ammo)
                {
                    if (rightHanded) tactsuitVr.PlayBackFeedback("ReceiveAmmo_L");
                    else tactsuitVr.PlayBackFeedback("ReceiveAmmo_R");
                }
            }
        }

        [HarmonyPatch(typeof(InventoryRoot), "SetHandednessPoses", new Type[] { typeof(Handedness) })]
        public class bhaptics_SetHandedness
        {
            [HarmonyPostfix]
            public static void Postfix(Handedness handedness)
            {
                rightHanded = (handedness == Handedness.Right);
            }
        }
        #endregion

        #region Damage and Health

        private static (float, float) getAngleAndShift(Transform player, Vector3 hitPoint, Quaternion headRotation)
        {
            Vector3 patternOrigin = new Vector3(0f, 0f, 1f);
            // y is "up", z is "forward" in local coordinates
            Vector3 hitPosition = hitPoint - player.position;
            Quaternion PlayerRotation = player.rotation * headRotation;
            Vector3 playerDir = PlayerRotation.eulerAngles;
            // We only want rotation correction in y direction (left-right), top-bottom and yaw we can leave
            Vector3 flattenedHit = new Vector3(hitPosition.x, 0f, hitPosition.z);
            float earlyhitAngle = Vector3.Angle(flattenedHit, patternOrigin);
            Vector3 earlycrossProduct = Vector3.Cross(flattenedHit, patternOrigin);
            if (earlycrossProduct.y > 0f) { earlyhitAngle *= -1f; }
            float myRotation = earlyhitAngle - playerDir.y;
            myRotation *= -1f;
            if (myRotation < 0f) { myRotation = 360f + myRotation; }

            float hitShift = hitPosition.y;
            //tactsuitVr.LOG("Hitshift: " + hitShift.ToString());
            float upperBound = 1.7f;
            float lowerBound = 1.2f;
            if (hitShift > upperBound) { hitShift = 0.5f; }
            else if (hitShift < lowerBound) { hitShift = -0.5f; }
            // ...and then spread/shift it to [-0.5, 0.5], which is how bhaptics expects it
            else { hitShift = (hitShift - lowerBound) / (upperBound - lowerBound) - 0.5f; }

            return (myRotation, hitShift);
        }

        [HarmonyPatch(typeof(PlayerActor), "OnDamageApply", new Type[] { typeof(ProjectileHitInfo), typeof(ProjectileService.DamageResult) })]
        public class bhaptics_PlayerHit
        {
            [HarmonyPostfix]
            public static void Postfix(PlayerActor __instance, ProjectileHitInfo info, ProjectileService.DamageResult damageResult)
            {
                Quaternion headRotation = __instance.headRotation();
                float hitAngle;
                float hitShift;
                string damageType = "BulletHit";
                if (info.damageData.instantKill) damageType = "Impact";
                if (info.damageData.isExplodingShot) damageType = "Impact";
                if ((info.damageData.isExplosion) || (info.damageData.isGrenade)) { tactsuitVr.PlayBackFeedback("Explosion"); return; }
                if (info.damageData.isMelee) damageType = "BladeHit";
                if (info.damageData.isPoison) damageType = "Poison";
                if (info.damageData.isSpell) damageType = "Impact";
                (hitAngle, hitShift) = getAngleAndShift(__instance.transform, info.hitPosition, headRotation);
                tactsuitVr.PlayBackHit(damageType, hitAngle, hitShift);
            }
        }


        [HarmonyPatch(typeof(Consumable_Medkit), "OnConsumableApplyEffect", new Type[] { typeof(Actor) })]
        public class bhaptics_PlayerMedKit
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                tactsuitVr.PlayBackFeedback("Healing");
            }
        }

        #endregion

        #region Extra effects


        [HarmonyPatch(typeof(JuiceVolume), "FlashSettings", new Type[] { typeof(JuiceVolume.JuiceLayerName), typeof(float), typeof(float), typeof(float) })]
        public class bhaptics_Prayer
        {
            [HarmonyPostfix]
            public static void Postfix(JuiceVolume __instance, JuiceVolume.JuiceLayerName layerName, float fadeInSecs, float holdDurationSecs, float fadeOutSecs)
            {
                if (layerName == JuiceVolume.JuiceLayerName.PlayerTeleport) tactsuitVr.PlayBackFeedback("Teleport");
                if (layerName == JuiceVolume.JuiceLayerName.AbsorbSoul) tactsuitVr.PlayBackHit("AbsorbSoul", myRandom.NextFloat(0f, 360f), myRandom.NextFloat(-0.4f, 0.4f));
                if (layerName == JuiceVolume.JuiceLayerName.LevelUp) tactsuitVr.PlayBackFeedback("LevelUp");
                if (layerName == JuiceVolume.JuiceLayerName.AbsorbTarotCard) tactsuitVr.PlayBackFeedback("AbsorbTarotCard");
                if (layerName == JuiceVolume.JuiceLayerName.Prayer)
                {
                    if ((fadeInSecs == 0.1f) && (fadeOutSecs == 0.3f)) return;
                    //tactsuitVr.LOG("Numbers: " + fadeInSecs + " " + holdDurationSecs + " " + fadeOutSecs);
                    tactsuitVr.PlayBackFeedback("Prayer");
                }
            }
        }

        [HarmonyPatch(typeof(JuiceVolume), "TriggerAreaClearRingBurst", new Type[] { })]
        public class bhaptics_AreaClear
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                tactsuitVr.PlayBackFeedback("AreaClear");
            }
        }

        [HarmonyPatch(typeof(JuiceVolume), "FadeOut", new Type[] { typeof(JuiceVolume.JuiceLayerName), typeof(float), typeof(float) })]
        public class bhaptics_FadeInLayer
        {
            [HarmonyPostfix]
            public static void Postfix(JuiceVolume __instance, JuiceVolume.JuiceLayerName layerName)
            {
                if (layerName == JuiceVolume.JuiceLayerName.PlayerTeleport)
                {
                    tactsuitVr.PlayBackFeedback("Teleport");
                }
            }
        }

        #endregion

    }
}
