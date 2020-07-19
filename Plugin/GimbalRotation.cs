/* Copyright © 2013-2016, Elián Hanisch <lambdae2@gmail.com>
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */

using System.Collections;
using UnityEngine;

namespace RCSBuildAid
{
    public class GimbalRotation : MonoBehaviour
    {
        /* For mirrored parts SerializeField is needed */
        [SerializeField]
        ModuleGimbal gimbal;
        [SerializeField]
        Quaternion[] originalRotations;
        [SerializeField] 
        Quaternion[] finalRotations;
        [SerializeField]
        float startTime;
        bool rotating;

        const float speed = 2f;
        
        void Start ()
        {
            Debug.Assert (gimbal != null, "[RCSBA, GimbalRotation]: gimbal is null");

            initRotations();
            
            Events.DirectionChanged += switchDirection;
            Events.PluginToggled += onPluginToggled;
            Events.ShipModified += onShipModified;
            Events.ShipModified += updateRotation;
        }

        void OnDestroy ()
        {
            Events.DirectionChanged -= switchDirection;
            Events.PluginToggled -= onPluginToggled;
            Events.ShipModified -= onShipModified;
            Events.ShipModified -= updateRotation;
        }

        public static void addTo(GameObject obj)
        {
            if (obj.GetComponent<GimbalRotation> () != null) {
                /* already added */
                return;
            }
            var gimbals = obj.GetComponents<ModuleGimbal> ();
            for (int i = 0; i < gimbals.Length; i++) {
                var g = obj.AddComponent<GimbalRotation> ();
                g.gimbal = gimbals [i];
            }
        }

        void initRotations()
        {
            if (originalRotations == null) {
                originalRotations = new Quaternion[gimbal.gimbalTransforms.Count];
                finalRotations = new Quaternion[gimbal.gimbalTransforms.Count];
                for (int i = 0; i < gimbal.gimbalTransforms.Count; i++) {
                    var localRotation = gimbal.gimbalTransforms [i].localRotation;
                    originalRotations [i] = localRotation;
                    finalRotations [i] = localRotation;
                }
            }
        }

        void destroyRotations()
        {
            originalRotations = null;
            finalRotations = null;
        }

        void switchDirection(Direction direction)
        {
            updateRotation();
        }
        
        void onPluginToggled(bool value, bool byUser)
        {
            enabled = value;
            if (value) {
                updateRotation();
            }
        }

        void onShipModified()
        {
            Debug.Assert (gimbal != null, "[RCSBA, GimbalRotation]: gimbal != null");
            Debug.Assert (gimbal.gimbalTransforms != null, "[RCSBA, GimbalRotation]: gimbalTransforms != null");
            Debug.Assert (originalRotations != null, "[RCSBA, GimbalRotation]: originalRotations != null");

            /* needed for mods like SSTU that swap models and change the number of thrustTransforms */
            if (gimbal.gimbalTransforms.Count != originalRotations.Length) {
                destroyRotations();
                initRotations();
            }
        }

        float getGimbalRange ()
        {
            return gimbal.gimbalRange * gimbal.gimbalLimiter / 100f;
        }

        Vector3 getRotation() {
            Vector3 vector = RCSBuildAid.RotationVector;
            if (!gimbal.enablePitch) {
                var n = RCSBuildAid.ReferenceTransform.right;
                vector -= Vector3.Dot(vector, n) * n;
            }
            if (!gimbal.enableRoll) {
                var n = RCSBuildAid.ReferenceTransform.up;
                vector -= Vector3.Dot(vector, n) * n;
            }
            if (!gimbal.enableYaw) {
                var n = RCSBuildAid.ReferenceTransform.forward;
                vector -= Vector3.Dot(vector, n) * n;
            }
            return vector;
        }

        void updateRotation() 
        {
            calculateFinalRotations();
            startTime = Time.time; /* for the animation */
            if (!rotating) {
                rotating = true;
                StartCoroutine(RotateGimbal());
            }
        }
        
        IEnumerator RotateGimbal()
        {
            for (;;) {
                int test = 0;
                for (int i = 0; i < gimbal.gimbalTransforms.Count; i++) {
                    Transform t = gimbal.gimbalTransforms[i];
                    Quaternion finalRotation = finalRotations[i];
                    if (t.localRotation == finalRotation) {
                        test += 1;
                    } else {
                        t.localRotation = Quaternion.Lerp(t.localRotation, finalRotation,
                            (Time.time - startTime) * speed);
                    }
                }

                if (test == gimbal.gimbalTransforms.Count) {
                    rotating = false;
                    break;
                }

                yield return null;
            }
        }

        void calculateFinalRotations()
        {
            Debug.Assert (gimbal != null, "[RCSBA, GimbalRotation]: gimbal != null");
            Debug.Assert (gimbal.gimbalTransforms != null, "[RCSBA, GimbalRotation]: gimbalTransforms != null");
            Debug.Assert (originalRotations != null, "[RCSBA, GimbalRotation]: originalRotations != null");
            Debug.Assert (originalRotations.Length == gimbal.gimbalTransforms.Count, 
                "[RCSBA, GimbalRotation]: Number of quaternions doesn't match the number of transforms");

            for (int i = 0; i < gimbal.gimbalTransforms.Count; i++) {
                Transform t = gimbal.gimbalTransforms[i];
                if (gimbal.gimbalLock || (gimbal.part.inverseStage != RCSBuildAid.LastStage) 
                                      || (RCSBuildAid.Mode != PluginMode.Engine)) {
                    /* restore gimbal's position */
                    finalRotations[i] = originalRotations[i];
                } else {
                    float angle = getGimbalRange();
                    Vector3 rotationVector = getRotation();
                    /* Get the projection in the up vector, that one is for roll */
                    Vector3 up = RCSBuildAid.ReferenceTransform.up;
                    Vector3 roll = Vector3.Dot(rotationVector, up) * up;
                    if (roll.sqrMagnitude > 0.01f) {
                        int dir = (roll.normalized + up).magnitude > 1 ? 1 : -1; /* save roll direction */
                        /* translate roll into pitch/yaw rotation */
                        Vector3 distance = t.position - RCSBuildAid.ReferenceTransform.transform.position;
                        Vector3 newRoll = distance - Vector3.Dot(distance, roll.normalized) * roll.normalized;
                        newRoll *= dir;
                        /* update rotationVector */
                        rotationVector -= roll;
                        rotationVector += newRoll;
                    }

                    Vector3 pivot = t.InverseTransformDirection(rotationVector);
                    finalRotations[i] = originalRotations[i] * Quaternion.AngleAxis(angle, pivot);
                }
            }
        }
    }
}

