using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace Archer.EditorTools
{
    /// <summary>Repeatable live-editor integration checks. Does not exit or close the Editor.</summary>
    public static class LastRangerChecks
    {
        static IEnumerator _checks;
        static double _next;
        static int _lastFrame, _passed;
        static Mouse _mouse;
        static Monster _target;
        static float _interval;
        public static string Status { get; private set; } = "Not run";
        static readonly string ReportPath = "Temp/last-ranger-checks.txt";

        [MenuItem("Archer/Run Last Ranger Integration Checks")]
        public static void Run()
        {
            if(!EditorApplication.isPlaying || GameManager.Instance == null)
                throw new InvalidOperationException("Open Game.unity and enter Play mode first.");
            if(_checks != null) throw new InvalidOperationException("Checks already running.");
            _passed=0; _lastFrame=-1; _next=0;
            _interval=GameManager.Instance.SpawnInterval;
            Status="Running";
            File.WriteAllText(ReportPath,$"Last Ranger integration checks — Unity {Application.unityVersion}, {Screen.width}x{Screen.height}\n");
            _checks=Checks(); EditorApplication.update+=Tick;
        }

        static void Tick()
        {
            if(!EditorApplication.isPlaying) { Finish("FAIL: Play mode stopped during checks"); return; }
            if(EditorApplication.timeSinceStartup<_next || _lastFrame==Time.frameCount) return;
            _lastFrame=Time.frameCount;
            try
            {
                if(!_checks.MoveNext()) { Finish($"PASS: {_passed} assertions"); return; }
                _next=EditorApplication.timeSinceStartup+(_checks.Current is float seconds ? seconds : 0f);
            }
            catch(Exception e) { Finish("FAIL: "+e.Message); }
        }

        static void Check(bool condition, string label)
        {
            if(!condition) throw new Exception(label);
            _passed++; File.AppendAllText(ReportPath,"PASS "+label+"\n");
        }

        static void MouseEvent(Vector2 position, bool held)
        {
            var state = new MouseState { position=position };
            if(held) state=state.WithButton(MouseButton.Left);
            InputSystem.QueueStateEvent(_mouse,state);
        }

        static Vector2 ButtonCenter(string name)
        {
            foreach(var button in Hud.Instance.GetComponentsInChildren<Button>(true))
                if(button.name == name) return RectTransformUtility.WorldToScreenPoint(null,button.transform.position);
            throw new Exception("Button not found: "+name);
        }

        static IEnumerator Checks()
        {
            var gm=GameManager.Instance; var archer=gm.Archer;
            gm.Restart(); gm.SpawnInterval=1000f;
            float baseCharge = archer.ChargeTime, baseRecovery = gm.ArrowRegenInterval;
            _mouse=InputSystem.AddDevice<Mouse>("Last Ranger checks");
            MouseEvent(Vector2.zero,false); yield return 0.1f;
            gm.StartRaid();
            Check(gm.RaidNumber==1,"Duplicate raid start rejected");
            int arrows=gm.Arrows;
            gm.TogglePause();
            Check(gm.State==GameState.Paused && Time.timeScale==0,"Pause freezes simulation");
            Check(!gm.TryConsumeArrow() && gm.Arrows==arrows,"Cannot shoot while paused");
            gm.TogglePause();
            Check(gm.State==GameState.Playing && Time.timeScale==1,"Resume restores simulation");
            // Real physics: a horizontal arrow must lose height and point downward.
            var gravityMaterial=Prim.Material(Color.white);
            var falling=Arrow.Create(gm.MonsterParent,new Vector3(20,20,10),Vector3.forward*30,10,gravityMaterial);
            float physicsStart=Time.fixedTime;
            yield return 0.4f;
            float flight=Time.fixedTime-physicsStart;
            float expectedY=20+0.5f*Physics.gravity.y*flight*flight;
            Check(falling.transform.position.y<19.6f && Mathf.Abs(falling.transform.position.y-expectedY)<0.25f,"Real arrow follows gravity's parabolic drop");
            Check(falling.GetComponent<Rigidbody>().linearVelocity.y<-2 && falling.transform.forward.y<0,"Arrow points downward as it falls");
            Check(falling.GetComponent<TrailRenderer>()!=null,"Fired arrow has a short flight trail");
            UnityEngine.Object.Destroy(falling.gameObject); UnityEngine.Object.Destroy(gravityMaterial);
            _target=Monster.Create(gm.MonsterParent,new Vector3(0,0,3),gm.ArcherTarget,MonsterRole.Raider,120,0,0,1,new Color(0.5f,0.7f,0.3f));
            yield return null;
            Transform head=null;
            foreach(var part in _target.GetComponentsInChildren<Transform>())
                if(part.name=="Head") { head=part; break; }
            Vector2 point=Camera.main.WorldToScreenPoint((head!=null?head.position:_target.transform.position) + Vector3.up * 0.3f);
            MouseEvent(point,true); yield return 0.2f;
            Check(archer.IsAiming,"Fresh pointer press draws bow");
            Check(archer.HasPrediction,"Trajectory predicts an impact");
            yield return archer.ChargeTime + 0.1f;
            Check(archer.Charge01>0.99f,"Configured draw reaches full charge");
            MouseEvent(point,false); yield return 0.65f;
            Check(gm.ShotsFired==1,"Release fires exactly one shot");
            Check(gm.ShotsHit==1 && _target.Health<120,"Real ballistic projectile hits aimed enemy");
            Check(_target.Health<70,"Headshot awards precision damage");
            Check(gm.Headshots==1 && gm.HitStreak==1 && gm.Score==75,"Physical headshot awards precision score and streak");
            UnityEngine.Object.Destroy(_target.gameObject); _target=null;

            // A shield should intercept the same physical projectile before the body.
            _target=Monster.Create(gm.MonsterParent,new Vector3(0,0,10),gm.ArcherTarget,MonsterRole.Shield,120,0,0,1,new Color(0.5f,0.7f,0.3f));
            yield return 0.5f; // Let the enemy finish facing the gate before targeting its shield.
            Transform shieldPart=null;
            foreach(var part in _target.GetComponentsInChildren<Transform>())
                if(part.name=="Shield") { shieldPart=part; break; }
            Check(shieldPart!=null,"Shield goblin builds its shield rig");
            Vector3 shield=shieldPart.position;
            point=Camera.main.WorldToScreenPoint(shield + Vector3.up * 1.1f);
            MouseEvent(point,true); yield return archer.ChargeTime + 0.1f;
            MouseEvent(point,false); yield return 0.65f;
            Check(_target.Health==120,"Shield blocks a body shot");
            Check(gm.HitStreak==0 && gm.Score==75 && gm.ShotsHit==1,"Shield block breaks streak without awarding points");
            UnityEngine.Object.Destroy(_target.gameObject); _target=null;

            // Exercise the Canvas controls through the Input System as a player would.
            int previousShots=gm.ShotsFired;
            MouseEvent(new Vector2(Screen.width*0.5f,Screen.height*0.5f),true); yield return 0.15f;
            MouseEvent(ButtonCenter("Cancel shot"),true); yield return 0.1f;
            MouseEvent(ButtonCenter("Cancel shot"),false); yield return 0.1f;
            Check(!archer.IsAiming && gm.ShotsFired==previousShots,"Canvas cancel lowers the bow without firing");
            point=ButtonCenter("Pause");
            MouseEvent(point,true); yield return 0.1f;
            MouseEvent(point,false); yield return 0.1f;
            Check(gm.State==GameState.Paused,"Canvas pause button receives pointer clicks");
            point=ButtonCenter("Primary action");
            MouseEvent(point,true); yield return 0.1f;
            MouseEvent(point,false); yield return 0.1f;
            Check(gm.State==GameState.Playing && !archer.IsAiming,"Canvas resume does not also draw a shot");
            gm.Arrows=0;
            yield return gm.ArrowRegenInterval + 0.15f;
            Check(gm.Arrows>0,"An empty quiver regenerates ammunition");
            gm.Arrows=gm.MaxArrows;

            // Death while drawing and restart with a live projectile.
            MouseEvent(new Vector2(Screen.width*0.5f,Screen.height*0.5f),true); yield return 0.15f;
            Check(archer.IsAiming,"Second gesture can draw again");
            var shaft=Prim.Material(Color.white);
            Arrow.Create(gm.MonsterParent,new Vector3(20,5,20),Vector3.forward,10,shaft);
            gm.DamageArcher(10000);
            MouseEvent(Vector2.zero,false); yield return null;
            Check(gm.State==GameState.GameOver && !archer.IsAiming,"Death cancels a drawn shot");
            int shots=gm.ShotsFired;
            yield return null;
            Check(gm.ShotsFired==shots,"Release after death cannot fire");
            gm.Restart(); yield return 0.1f;
            Check(UnityEngine.Object.FindObjectsByType<Arrow>(FindObjectsSortMode.None).Length==0,"Restart removes all live projectiles");
            Check(gm.ShotsFired==0 && gm.ShotsHit==0 && gm.Kills==0 && gm.RaidsCleared==0,"Restart resets run statistics");
            Check(gm.Score==0 && gm.Headshots==0 && gm.BestStreak==0,"Restart resets score and precision statistics");
            Check(gm.Health==gm.MaxHealth && gm.Arrows==gm.MaxArrows,"Restart restores health and quiver");
            UnityEngine.Object.Destroy(shaft);
            gm.SpawnInterval=0.015f;

            for(int raid=1;raid<=GameManager.TotalRaids;raid++)
            {
                if(raid==2) Check(gm.ObjectiveKind==RaidObjectiveKind.StopRunners && gm.ObjectiveTarget==2,"Raid two teaches stopping two runners");
                if(raid==5) Check(gm.ObjectiveKind==RaidObjectiveKind.InterruptBrute && gm.ObjectiveTarget==2,"Raid five finale requires two brute interruptions");
                if(raid==8) Check(gm.ObjectiveKind==RaidObjectiveKind.StopRunners && gm.ObjectiveTarget==5,"Raid eight requires stopping every runner");
                if(raid==9) Check(gm.ObjectiveKind==RaidObjectiveKind.InterruptBrute && gm.ObjectiveTarget==1,"Raid nine rehearses the brute interrupt");
                if(raid==10) Check(gm.ObjectiveKind==RaidObjectiveKind.InterruptBrute && gm.ObjectiveTarget==2,"Raid ten combines the brute interrupt finale");
                // Kill each actually spawned enemy; queue counts must prevent an early clear.
                double deadline=EditorApplication.timeSinceStartup+8;
                while(gm.State==GameState.Playing && EditorApplication.timeSinceStartup<deadline)
                {
                    foreach(var monster in gm.ActiveMonsters) monster.TakeDamage(10000);
                    yield return 0.04f;
                }
                Check(gm.RaidsCleared==raid,"Raid "+raid+" clears exactly once after every queued enemy");
                if(raid==2 || raid==8) Check(gm.ObjectiveComplete && gm.ObjectivesCompleted>0,"Stopping every runner completes the raid objective");
                if(raid==5 || raid==9 || raid==10) Check(gm.ObjectiveFailed,"Killing the brute without enough interrupts fails the optional objective");
                if(raid<GameManager.TotalRaids)
                {
                    Check(gm.State==GameState.Intermission && gm.Offers.Count==2,"Cleared raid offers two supplies");
                    int number=gm.RaidNumber;
                    gm.StartRaid(); Check(gm.RaidNumber==number,"Unchosen supplies cannot be skipped");
                    var chosen=gm.Offers[0];
                    float beforeCharge=archer.ChargeTime, beforeRecovery=gm.ArrowRegenInterval;
                    if(raid==1)
                    {
                        double overlayDeadline=EditorApplication.timeSinceStartup+4;
                        while(!Hud.Instance.OverlayVisible && EditorApplication.timeSinceStartup<overlayDeadline)
                            yield return 0.05f;
                        Check(Hud.Instance.OverlayVisible,"Intermission card appears after the clear beat");
                        point=ButtonCenter("Upgrade A");
                        MouseEvent(point,true); yield return 0.1f;
                        MouseEvent(point,false); yield return 0.1f;
                    }
                    else gm.ChooseUpgrade(chosen);
                    Check(gm.Offers.Count==0,"One supply choice consumes both offers");
                    if(chosen==UpgradeKind.QuickDraw) Check(archer.ChargeTime<beforeCharge,"Quickdraw supply reduces draw duration when selected");
                    if(chosen==UpgradeKind.Recovery) Check(gm.ArrowRegenInterval<beforeRecovery,"Recovery supply reduces recovery interval when selected");
                    float afterDamage=archer.ArrowDamage, afterHealth=gm.MaxHealth; int afterCapacity=gm.MaxArrows;
                    gm.ChooseUpgrade(chosen);
                    Check(archer.ArrowDamage==afterDamage && gm.MaxHealth==afterHealth && gm.MaxArrows==afterCapacity,"Supply cannot be taken twice");
                    gm.StartRaid();
                }
            }
            Check(gm.Score>0,"Completing raids awards score");
            Check(gm.State==GameState.Victory && gm.RaidNumber==GameManager.TotalRaids,"Tenth raid ends in Chapter Two cleared");
            gm.StartRaid(); Check(gm.RaidNumber==GameManager.TotalRaids,"Victory cannot start an unintended eleventh raid");
            int kills=gm.Kills;
            Check(!gm.TryConsumeArrow(),"Victory rejects shooting");
            gm.Restart(); yield return 0.1f;
            Check(gm.RaidNumber==1 && gm.RaidsCleared==0,"Restart from victory begins a clean run");
            Check(archer.ChargeTime==baseCharge && gm.ArrowRegenInterval==baseRecovery,"Restart restores draw and recovery rates");
            Check(archer.ArrowDamage==34 && gm.MaxHealth==100 && gm.MaxArrows==6,"Restart restores baseline upgrades");
        }

        static void Finish(string status)
        {
            EditorApplication.update-=Tick; _checks=null;
            Status=status; File.AppendAllText(ReportPath,status+"\n");
            if(_mouse!=null) { InputSystem.RemoveDevice(_mouse); _mouse=null; }
            if(_target!=null) { UnityEngine.Object.Destroy(_target.gameObject); _target=null; }
            if(GameManager.Instance!=null) { GameManager.Instance.SpawnInterval=_interval; GameManager.Instance.Restart(); }
            Time.timeScale=1f;
            if(status.StartsWith("PASS")) Debug.Log("[Last Ranger checks] "+status);
            else Debug.LogError("[Last Ranger checks] "+status);
        }
    }
}
