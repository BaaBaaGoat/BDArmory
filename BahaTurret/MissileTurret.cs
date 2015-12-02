﻿using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BahaTurret
{
	public class MissileTurret : PartModule
	{
		[KSPField]
		public string finalTransformName;
		public Transform finalTransform;

		[KSPField]
		public int turretID = 0;

		ModuleTurret turret;

		[KSPField(guiActive = true, guiName = "Enabled")]
		public bool turretEnabled = false;

		[KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Auto-Return")]
		public bool autoReturn = true;
		bool hasReturned = true;

		Coroutine returnRoutine;

		int missileCount = 0;
		MissileLauncher[] missileChildren;
		Transform[] missileTransforms;
		Transform[] missileReferenceTransforms;

		Dictionary<string,Vector3> comOffsets;

		public bool slaved = false;

		public Vector3 slavedTargetPosition;

		bool pausingAfterShot = true;
		float timeFired = 0;
		float pauseTime = 0.5f;

		ModuleRadar attachedRadar;
		bool hasAttachedRadar = false;
		[KSPField]
		public bool disableRadarYaw = false;
		[KSPField]
		public bool disableRadarPitch = false;

		MissileFire wm;
		public MissileFire weaponManager
		{
			get
			{
				if(!wm)
				{
					foreach(var mf in vessel.FindPartModulesImplementing<MissileFire>())
					{
						wm = mf;
						break;
					}
				}

				return wm;
			}
		}

		public void EnableTurret()
		{
			if(returnRoutine!=null)
			{
				StopCoroutine(returnRoutine);
				returnRoutine = null;
			}

			turretEnabled = true;
			hasReturned = false;

			if(hasAttachedRadar)
			{
				attachedRadar.lockingYaw = !disableRadarYaw;
				attachedRadar.lockingPitch = !disableRadarPitch;
			}
		}

		public void DisableTurret()
		{
			turretEnabled = false;
			if(autoReturn)
			{
				hasReturned = true;
				returnRoutine = StartCoroutine(ReturnRoutine());
			}

			if(hasAttachedRadar)
			{
				attachedRadar.lockingYaw = true;
				attachedRadar.lockingPitch = true;
			}
		}

		IEnumerator ReturnRoutine()
		{
			while(pausingAfterShot)
			{
				yield return new WaitForFixedUpdate();
			}

			while(!turret.ReturnTurret())
			{
				UpdateMissilePositions();
				yield return new WaitForFixedUpdate();
			}
		}

		public override void OnStart(StartState state)
		{
			base.OnStart(state);

			part.force_activate();

			foreach(var tur in part.FindModulesImplementing<ModuleTurret>())
			{
				if(tur.turretID == turretID)
				{
					turret = tur;
					break;
				}
			}

			attachedRadar = part.FindModuleImplementing<ModuleRadar>();
			if(attachedRadar) hasAttachedRadar = true;

			finalTransform = part.FindModelTransform(finalTransformName);

			UpdateMissileChildren();
		}

		public override void OnFixedUpdate()
		{
			base.OnFixedUpdate();



			if(turretEnabled)
			{
				hasReturned = false;

				if(missileCount == 0)
				{
					DisableTurret();
					return;
				}

				Aim();
				UpdateMissilePositions();

				if(!vessel.IsControllable)
				{
					DisableTurret();
				}

			}
			else
			{
				if(Quaternion.FromToRotation(finalTransform.forward, turret.yawTransform.parent.parent.forward) != Quaternion.identity)
				{
					UpdateMissilePositions();
				}

				if(autoReturn && !hasReturned)
				{
					DisableTurret();
				}



			}

			pausingAfterShot = (Time.time - timeFired < pauseTime);
		}

		void Aim()
		{
			UpdateTarget();

			if(slaved)
			{
				SlavedAim();
			}
			else
			{
				if(weaponManager && wm.guardMode)
				{
					return;
				}

				MouseAim();
			}
		}

		void UpdateTarget()
		{
			slaved = false;

			if(weaponManager)
			{
				ModuleRadar radar = wm.radar;
				if(radar && radar.radarEnabled && radar.slaveTurrets)
				{
					slaved = true;
					slavedTargetPosition = MissileGuidance.GetAirToAirFireSolution(wm.currentMissile, radar.lockedTarget.predictedPosition, radar.lockedTarget.velocity);
				}
			}
		}



		public void SlavedAim()
		{
			if(pausingAfterShot) return;

			turret.AimToTarget(slavedTargetPosition);
		}


		void MouseAim()
		{
			if(pausingAfterShot) return;

			Vector3 targetPosition;
			float maxTargetingRange = 5000;

			//MouseControl
			Vector3 mouseAim = new Vector3(Input.mousePosition.x/Screen.width, Input.mousePosition.y/Screen.height, 0);
			Ray ray = FlightCamera.fetch.mainCamera.ViewportPointToRay(mouseAim);
			RaycastHit hit;
			if(Physics.Raycast(ray, out hit, maxTargetingRange, 557057))
			{
				targetPosition = hit.point;

				//aim through self vessel if occluding mouseray
				Part p = hit.collider.gameObject.GetComponentInParent<Part>();
				if(p && p.vessel && p.vessel == vessel)
				{
					targetPosition = ray.direction * maxTargetingRange + FlightCamera.fetch.mainCamera.transform.position; 
				}
			}
			else
			{
				targetPosition = (ray.direction * (maxTargetingRange+(FlightCamera.fetch.Distance*0.75f))) + FlightCamera.fetch.mainCamera.transform.position;	
			}

			turret.AimToTarget(targetPosition);
		}

		public void UpdateMissileChildren()
		{
			missileCount = 0;

			//setup com dictionary
			if(comOffsets == null)
			{
				comOffsets = new Dictionary<string, Vector3>();
			}

			//destroy the existing reference transform objects
			if(missileReferenceTransforms != null)
			{
				for(int i = 0; i < missileReferenceTransforms.Length; i++)
				{
					if(missileReferenceTransforms[i])
					{
						GameObject.Destroy(missileReferenceTransforms[i].gameObject);
					}
				}
			}

			List<MissileLauncher> msl = new List<MissileLauncher>();
			List<Transform> mtfl = new List<Transform>();
			List<Transform> mrl = new List<Transform>();

			foreach(var child in part.children)
			{
				MissileLauncher ml = child.FindModuleImplementing<MissileLauncher>();
				Transform mTf = child.FindModelTransform("missileTransform");
				//fix incorrect hierarchy
				if(!mTf)
				{
					Transform modelTransform = ml.part.partTransform.FindChild("model");

					mTf = new GameObject("missileTransform").transform;
					Transform[] tfchildren = new Transform[modelTransform.childCount];
					for(int i = 0; i < modelTransform.childCount; i++)
					{
						tfchildren[i] = modelTransform.GetChild(i);
					}
					mTf.parent = modelTransform;
					mTf.localPosition = Vector3.zero;
					mTf.localRotation = Quaternion.identity;
					mTf.localScale = Vector3.one;
					for(int i = 0; i < tfchildren.Length; i++)
					{
						Debug.Log("MissileTurret moving transform: " + tfchildren[i].gameObject.name);
						tfchildren[i].parent = mTf;
					}
				}

				if(ml && mTf)
				{
					msl.Add(ml);
					mtfl.Add(mTf);
					Transform mRef = new GameObject().transform;
					mRef.position = mTf.position;
					mRef.rotation = mTf.rotation;
					mRef.parent = finalTransform;
					mrl.Add(mRef);

					ml.missileReferenceTransform = mTf;
					ml.missileTurret = this;

					ml.decoupleForward = true;
					ml.dropTime = 0;

					if(!comOffsets.ContainsKey(ml.part.partName))
					{
						comOffsets.Add(ml.part.partName, ml.part.CoMOffset);
					}

					missileCount++;
				}
			}

			missileChildren = msl.ToArray();
			missileTransforms = mtfl.ToArray();
			missileReferenceTransforms = mrl.ToArray();


		}

		void UpdateMissilePositions()
		{
			if(missileCount == 0)
			{
				return;
			}

			for(int i = 0; i < missileChildren.Length; i++)
			{
				if(missileTransforms[i] && missileChildren[i] && !missileChildren[i].hasFired)
				{
					missileTransforms[i].position = missileReferenceTransforms[i].position;
					missileTransforms[i].rotation = missileReferenceTransforms[i].rotation;

					Part missilePart = missileChildren[i].part;
					Vector3 newCoMOffset = missilePart.transform.InverseTransformPoint(missileTransforms[i].TransformPoint(comOffsets[missilePart.partName]));
					missilePart.CoMOffset = newCoMOffset;
				}
			}
		}


		public void FireMissile(int index)
		{
			if(index < missileCount && missileChildren != null && missileChildren[index] != null)
			{
				PrepMissileForFire(index);
			
				if(weaponManager)
				{
					wm.SendTargetDataToMissile(missileChildren[index]);
				}
				missileChildren[index].FireMissile();

				if(wm)
				{
					wm.UpdateList();
				}

				UpdateMissileChildren();

				timeFired = Time.time;
			}
		}

		public void FireMissile(MissileLauncher ml)
		{
			int index = IndexOfMissile(ml);
			if(index >= 0)
			{
				Debug.Log("Firing missile index: " + index);
				FireMissile(index);
			}
		}


		void PrepMissileForFire(int index)
		{
			Debug.Log("Prepping missile for turret fire.");
			missileTransforms[index].localPosition = Vector3.zero;
			missileTransforms[index].localRotation = Quaternion.identity;
			missileChildren[index].transform.position = missileReferenceTransforms[index].position;
			missileChildren[index].transform.rotation = missileReferenceTransforms[index].rotation;

			missileChildren[index].dropTime = 0;
			missileChildren[index].decoupleForward = true;

			missileChildren[index].part.CoMOffset = comOffsets[missileChildren[index].part.partName];

		}

		public void PrepMissileForFire(MissileLauncher ml)
		{
			int index = IndexOfMissile(ml);

			if(index < 0) return;

			PrepMissileForFire(index);
		}

		private int IndexOfMissile(MissileLauncher ml)
		{
			if(missileCount == 0) return -1;

			for(int i = 0; i < missileCount; i++)
			{
				if(missileChildren[i] && missileChildren[i] == ml)
				{
					return i;
				}
			}

			return -1;
		}


	}
}

