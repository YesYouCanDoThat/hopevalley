using Network;
using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Configuration;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("SpaceNeedleLifts", "k1lly0u", "0.1.1")]
    class SpaceNeedleLifts : RustPlugin
    {
        #region Fields
        private StoredData storedData;
        private DynamicConfigFile data;

        private List<NeedleLift> lifts = new List<NeedleLift>();

        private static Vector3 buttonLowerOffset = new Vector3(1.45f, -3.45f, -1.62f);
        private static Vector3 buttonUpperOffset = new Vector3(-2.125f, 205.62f, -0.089f);

        private const string LIFT_PREFAB = "assets/content/structures/lifts/lift_cabin_27.prefab";
        private const string BUTTON_PREFAB = "assets/prefabs/io/electric/switches/pressbutton/pressbutton.prefab";

        private const string LOCK_DENIED = "assets/prefabs/locks/keypad/effects/lock.code.denied.prefab";
        private const string LOCK_ALLOW = "assets/prefabs/locks/keypad/effects/lock.code.updated.prefab";

        private const float APPROX_DISTANCE = 208.94f;
        #endregion

        #region Oxide Hooks
        private void Loaded()
        {
            data = Interface.Oxide.DataFileSystem.GetFile("spaceneedlelifts");
            LoadData();
        }

        private void OnServerInitialized()
        {
            if (storedData.lifts.Count == 0)
                FindValidLifts();
            else CreateLifts();
        }

        private void OnNewSave(string filename)
        {
            storedData = new StoredData();
            SaveData();
        }

        private object OnLiftUse(ProceduralLift lift, BasePlayer player)
        {
            NeedleLift needleLift = lift.GetComponent<NeedleLift>();
            if (needleLift != null)
            {
                needleLift.OnLiftUse();
                return true;
            }

            return null;
        }

        private void OnButtonPress(PressButton button, BasePlayer player)
        {
            ElevatorButton elevatorButton = button.GetComponent<ElevatorButton>();
            if (elevatorButton != null)
                elevatorButton.OnButtonPressed();
        }

        private void Unload()
        {
            for (int i = lifts.Count - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(lifts[i]);

            lifts.Clear();
        }
        #endregion

        #region Functions
        private void CreateLifts()
        {
            if (storedData.lifts.Count == 0)
            {
                Puts("No lifts saved in data file");
                return;
            }

            for (int i = 0; i < storedData.lifts.Count; i++)            
                CreateLift(storedData.lifts[i]);            
        }

        private void FindValidLifts()
        {
            Puts("Finding lifts...");

            ProceduralLift[] lifts = UnityEngine.Object.FindObjectsOfType<ProceduralLift>();
            if (lifts?.Length == 0)
                return;

            for (int i = 0; i < lifts.Length; i++)
            {
                ProceduralLift target = lifts[i];
                if (target == null || target.IsDestroyed)
                    continue;

                Vector3 targetPosition = target.transform.position;

                for (int y = 0; y < lifts.Length; y++)
                {
                    ProceduralLift other = lifts[y];
                    if (other == null || other == target || other.IsDestroyed)
                        continue;

                    Vector3 otherPosition = other.transform.position;

                    if (Approximately(otherPosition.x, targetPosition.x) && Approximately(otherPosition.z, targetPosition.z) && Approximately(Vector3.Distance(otherPosition, targetPosition), APPROX_DISTANCE))
                    {
                        Vector3 bottom = otherPosition.y < targetPosition.y ? otherPosition : targetPosition;
                        Vector3 top = otherPosition.y > targetPosition.y ? otherPosition : targetPosition;
                        float rotationBottom = otherPosition.y < targetPosition.y ? other.transform.rotation.eulerAngles.y : target.transform.rotation.eulerAngles.y;
                        float rotationTop = otherPosition.y > targetPosition.y ? other.transform.rotation.eulerAngles.y : target.transform.rotation.eulerAngles.y;

                        RecursiveKill(target);
                        RecursiveKill(other);

                        Puts("Found Lift at Space Needle");

                        storedData.lifts.Add(new StoredData.LiftPositions(bottom, top, rotationBottom, rotationTop));
                    }
                }
            }

            if (storedData.lifts.Count == 0)
            {
                Puts("No lifts found...");
                return;
            }

            SaveData();
            CreateLifts();
        }

        private static void RecursiveKill(BaseEntity entity)
        {
            for (int i = entity.children.Count - 1; i >= 0; i--)
            {
                BaseEntity child = entity.children[i];

                if (child != null)
                    RecursiveKill(child);
            }

            if (entity != null && !entity.IsDestroyed)
            {
                BaseEntity.saveList.Remove(entity);
                entity.Kill();
            }
        }

        private static bool Approximately(float a, float b, float threshold = 0.5f)
        {
            return ((a - b) < 0 ? ((a - b) * -1) : (a - b)) <= threshold;
        }

        private void CreateLift(StoredData.LiftPositions data)
        {
            ProceduralLift lift = GameManager.server.CreateEntity(LIFT_PREFAB, data.bottom, Quaternion.Euler(0f, data.rotationBottom, 0f)) as ProceduralLift;
            lift.enableSaving = false;
            lift.Spawn();

            NeedleLift needleLift = lift.gameObject.AddComponent<NeedleLift>();
            needleLift.Initialize(data);           
            lifts.Add(needleLift);

            Puts($"Created Lift at Space Needle {lift.transform.position}");
        } 
        #endregion

        private class NeedleLift : MonoBehaviour
        {
            public ProceduralLift Lift { get; private set; }
            public Transform CabinTransform { get; private set; }

            public PressButton ButtonLower { get; private set; }
            public PressButton ButtonUpper { get; private set; }

            private Vector3 bottom;
            private Vector3 top;
            private float rotationBottom;
            private float rotationTop;

            private float timeTaken = 0;
            private float timeToTake;

            internal bool directionUp = false;
            internal bool isMoving = false;

            private void Awake()
            {
                Lift = GetComponent<ProceduralLift>();
                CabinTransform = Lift.cabin.transform;

                Lift.enabled = false;
                enabled = false;

                for (int i = Lift.children.Count - 1; i >= 0; i--)                
                    Lift.children[i].EnableSaving(false);                
            }

            private void OnDestroy()
            {
                if (ButtonLower != null && !ButtonLower.IsDestroyed)
                    ButtonLower.Kill();

                if (ButtonUpper != null && !ButtonUpper.IsDestroyed)
                    ButtonUpper.Kill();

                if (Lift != null && !Lift.IsDestroyed)
                    RecursiveKill(Lift);
            }

            public void Initialize(StoredData.LiftPositions data)
            {
                this.bottom = data.bottom;
                this.top = data.top;
                this.rotationBottom = data.rotationBottom;
                this.rotationTop = data.rotationTop;

                timeToTake = (top.y - bottom.y) / configData.MovementSpeed;

                CreateButtons();

                enabled = true;
            }

            private void CreateButtons()
            {
                Vector3 position = Lift.transform.TransformPoint(buttonLowerOffset);

                ButtonLower = GameManager.server.CreateEntity(BUTTON_PREFAB, position, Lift.transform.rotation * Quaternion.Euler(0f, 180f, 0f)) as PressButton;
                ButtonLower.enableSaving = false;
                ButtonLower.Spawn();

                ButtonLower.gameObject.AddComponent<ElevatorButton>().Register(this, true);

                position = Lift.transform.TransformPoint(buttonUpperOffset);

                ButtonUpper = GameManager.server.CreateEntity(BUTTON_PREFAB, position, Lift.transform.rotation * Quaternion.Euler(0f, -137.8f, 0f)) as PressButton;
                ButtonUpper.enableSaving = false;
                ButtonUpper.Spawn();

                ButtonUpper.gameObject.AddComponent<ElevatorButton>().Register(this, false);
            }

            private void Update()
            {
                if (!isMoving)
                    return;

                timeTaken += Time.deltaTime;

                float delta = Mathf.InverseLerp(0, timeToTake, timeTaken);

                if (delta >= 1f)
                {
                    Lift.SetFlag(BaseEntity.Flags.Busy, false, false, true);
                    Lift.SendNetworkUpdateImmediate(false);

                    isMoving = false;
                }

                Lift.transform.position = Vector3.Lerp(bottom, top, directionUp ? delta : 1f - delta);
                Lift.transform.rotation = Quaternion.Euler(0f, Mathf.Lerp(rotationBottom, rotationTop, directionUp ? delta : 1f - delta), 0f);

                NetworkUpdate();
            }

            public void OnLiftUse()
            {
                if (!isMoving)
                {
                    timeTaken = 0;

                    directionUp = !directionUp;

                    isMoving = true;

                    Lift.SetFlag(BaseEntity.Flags.Busy, true, false, true);
                    Lift.SendNetworkUpdateImmediate(false);
                }
            }

            private void NetworkUpdate()
            {
                if (Lift.net.group.subscribers.Count == 0)
                    return;

                if (Net.sv.write.Start())
                {
                    Net.sv.write.PacketID(Message.Type.GroupChange);
                    Net.sv.write.EntityID(Lift.net.ID);
                    Net.sv.write.GroupID(Lift.net.group.ID);
                    Net.sv.write.Send(new SendInfo(Lift.net.group.subscribers));
                }

                if (Net.sv.write.Start())
                {
                    Net.sv.write.PacketID(Message.Type.EntityPosition);
                    Net.sv.write.EntityID(Lift.net.ID);
                    Net.sv.write.Vector3(Lift.GetNetworkPosition());
                    Net.sv.write.Vector3(Lift.GetNetworkRotation().eulerAngles);
                    Net.sv.write.Float(Lift.GetNetworkTime());

                    SendInfo info = new SendInfo(Lift.net.group.subscribers);
                    info.method = SendMethod.ReliableUnordered;
                    info.priority = Priority.Immediate;
                    Net.sv.write.Send(info);
                }
            }
        }

        private class ElevatorButton : MonoBehaviour
        {
            public PressButton Button { get; private set; }

            private NeedleLift lift;
            private bool isLower;

            private void Awake()
            {
                Button = GetComponent<PressButton>();
            }

            public void Register(NeedleLift lift, bool isLower)
            {
                this.lift = lift;
                this.isLower = isLower;
            }

            public void OnButtonPressed()
            {                
                if (!lift.isMoving && ((isLower && lift.directionUp) || (!isLower && !lift.directionUp)))
                {
                    lift.OnLiftUse();
                    Effect.server.Run(LOCK_ALLOW, transform.position);
                }
                else Effect.server.Run(LOCK_DENIED, transform.position);
            }
        }

        #region Config        
        private static ConfigData configData;

        private class ConfigData
        {
            public float MovementSpeed { get; set; }
            public Oxide.Core.VersionNumber Version { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            configData = Config.ReadObject<ConfigData>();

            if (configData.Version < Version)
                UpdateConfigValues();

            Config.WriteObject(configData, true);
        }

        protected override void LoadDefaultConfig() => configData = GetBaseConfig();

        private ConfigData GetBaseConfig()
        {
            return new ConfigData
            {
                MovementSpeed = 2.5f,
                Version = Version
            };
        }

        protected override void SaveConfig() => Config.WriteObject(configData, true);

        private void UpdateConfigValues()
        {
            PrintWarning("Config update detected! Updating config values...");

            configData.Version = Version;
            PrintWarning("Config update completed!");
        }

        #endregion

        #region Data Management
        private void SaveData() => data.WriteObject(storedData);

        private void LoadData()
        {
            try
            {
                storedData = data.ReadObject<StoredData>();
            }
            catch
            {
                storedData = new StoredData();
            }
        }

        private class StoredData
        {
            public List<LiftPositions> lifts = new List<LiftPositions>();
            
            public class LiftPositions
            {
                public VectorData bottom;
                public VectorData top;

                public float rotationBottom;
                public float rotationTop;

                public LiftPositions() { }

                public LiftPositions(Vector3 bottom, Vector3 top, float rotationBottom, float rotationTop)
                {
                    this.bottom = bottom;
                    this.top = top;
                    this.rotationBottom = rotationBottom;
                    this.rotationTop = rotationTop;
                }
            }
        }

        public class VectorData
        {
            public float x, y, z;

            public VectorData() { }

            public VectorData(float x, float y, float z)
            {
                this.x = x;
                this.y = y;
                this.z = z;
            }

            public static implicit operator VectorData(Vector3 v)
            {
                return new VectorData(v.x, v.y, v.z);
            }

            public static implicit operator Vector3(VectorData v)
            {
                return new Vector3(v.x, v.y, v.z);
            }
        }
        #endregion
    }
}
