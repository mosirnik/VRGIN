using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Valve.VR;
using VRGIN.Controls;
using VRGIN.Core;
using VRGIN.Helpers;
using UnityEngine;
using static SteamVR_Controller;

namespace VRGIN.Controls
{
    public class GrabAction
    {
        private Transform transform => Owner.transform;
        private Controller Owner { get; set; }
        private Controller OtherController { get; set; }
        private Device Controller { get; set; }
        private Controller.Lock _OtherLock;

        private TravelDistanceRumble _TravelRumble;

        private float _GripStartTime;
        private const float GRIP_TIME_THRESHOLD = 0.1f;
        private const float GRIP_DIFF_THRESHOLD = 0.01f;
        private Vector3 _PrevControllerPos;
        private Quaternion _PrevControllerRot;
        private const EVRButtonId SECONDARY_SCALE_BUTTON = EVRButtonId.k_EButton_SteamVR_Trigger;
        private const EVRButtonId SECONDARY_ROTATE_BUTTON = EVRButtonId.k_EButton_Grip;
        private bool _ScaleInitialized;
        private bool _RotationInitialized;
        private float _InitialControllerDistance;
        private float _InitialIPD;
        private Vector3 _PrevFromTo;
        private ulong _ButtonMask;

        public GrabAction(Controller controller, Device device, ulong buttonMask)
        {
            Owner = controller;
            Controller = device;
            OtherController = controller.Other;

            // Prepare rumble definitions
            _TravelRumble = new TravelDistanceRumble(500, 0.1f, transform);
            _TravelRumble.UseLocalPosition = true;
            _TravelRumble.Reset();
            Owner.StartRumble(_TravelRumble);

            _GripStartTime = Time.unscaledTime;
            _PrevControllerPos = transform.position;
            _PrevControllerRot = transform.rotation;
            _ButtonMask = buttonMask;
        }

        public enum Status
        {
            Continue,
            DoneQuick,
            DoneSlow,
        }

        public void Destroy()
        {
            // Always stop rumbling when we're disabled
            Owner.StopRumble(_TravelRumble);
            if (HasOtherLock())
            {
                VRLog.Info("Releasing lock on other controller!");
                _OtherLock.SafeRelease();
            }
        }

        public Status HandleGrabbing()
        {
            if (OtherController.IsTracking && !HasOtherLock())
            {
                OtherController.TryAcquireFocus(out _OtherLock);
            }

            if (HasOtherLock() && OtherController.Input.GetPressDown(SECONDARY_SCALE_BUTTON))
            {
                _ScaleInitialized = false;
            }

            if (HasOtherLock() && OtherController.Input.GetPressDown(SECONDARY_ROTATE_BUTTON))
            {
                _RotationInitialized = false;
            }

            if (!Controller.GetPress(_ButtonMask))
            {
                if (Time.unscaledTime - _GripStartTime < GRIP_TIME_THRESHOLD)
                {
                    return Status.DoneQuick;
                }
                return Status.DoneSlow;
            }

            if (HasOtherLock() && (OtherController.Input.GetPress(SECONDARY_ROTATE_BUTTON) || OtherController.Input.GetPress(SECONDARY_SCALE_BUTTON)))
            {
                var newFromTo = (OtherController.transform.position - transform.position).normalized;

                if (OtherController.Input.GetPress(SECONDARY_SCALE_BUTTON))
                {
                    InitializeScaleIfNeeded();
                    var controllerDistance = Vector3.Distance(OtherController.transform.position, transform.position) * (_InitialIPD / VR.Settings.IPDScale);
                    float ratio = controllerDistance / _InitialControllerDistance;
                    VR.Settings.IPDScale = ratio * _InitialIPD;
                }

                if (OtherController.Input.GetPress(SECONDARY_ROTATE_BUTTON))
                {
                    InitializeRotationIfNeeded();
                    var angleDiff = Calculator.Angle(_PrevFromTo, newFromTo) * VR.Settings.RotationMultiplier;
                    VR.Camera.SteamCam.origin.transform.RotateAround(VR.Camera.Head.position, Vector3.up, angleDiff);// Mathf.Max(1, Controller.velocity.sqrMagnitude) );

                }

                _PrevFromTo = (OtherController.transform.position - transform.position).normalized;
            }
            else
            {
                var diffPos = transform.position - _PrevControllerPos;
                var diffRot = Quaternion.Inverse(_PrevControllerRot * Quaternion.Inverse(transform.rotation)) * (transform.rotation * Quaternion.Inverse(transform.rotation));
                if (Time.unscaledTime - _GripStartTime > GRIP_TIME_THRESHOLD || Calculator.Distance(diffPos.magnitude) > GRIP_DIFF_THRESHOLD)
                {
                    var forwardA = Vector3.forward;
                    var forwardB = diffRot * Vector3.forward;
                    var angleDiff = Calculator.Angle(forwardA, forwardB) * VR.Settings.RotationMultiplier;

                    VR.Camera.SteamCam.origin.transform.position -= diffPos;
                    //VRLog.Info("Rotate: {0}", NormalizeAngle(diffRot.eulerAngles.y));
                    if (!VR.Settings.GrabRotationImmediateMode && Controller.GetPress(ButtonMask.Trigger | ButtonMask.Touchpad))
                    {
                        VR.Camera.SteamCam.origin.transform.RotateAround(VR.Camera.Head.position, Vector3.up, -angleDiff);
                    }

                    _GripStartTime = 0; // To make sure that pos is not reset
                }
            }
            
            if(VR.Settings.GrabRotationImmediateMode && Controller.GetPressUp(ButtonMask.Trigger | ButtonMask.Touchpad))
            {
                // Rotate
                var originalLookDirection = Vector3.ProjectOnPlane(transform.position - VR.Camera.Head.position, Vector3.up).normalized;
                var currentLookDirection = Vector3.ProjectOnPlane(VR.Camera.Head.forward, Vector3.up).normalized;
                var angleDeg = Calculator.Angle(originalLookDirection, currentLookDirection);

                VR.Camera.SteamCam.origin.transform.RotateAround(VR.Camera.Head.position, Vector3.up, angleDeg);
            }

            _PrevControllerPos = transform.position;
            _PrevControllerRot = transform.rotation;

            return Status.Continue;
        }

        private void InitializeScaleIfNeeded()
        {
            if (!_ScaleInitialized)
            {
                _InitialControllerDistance = Vector3.Distance(OtherController.transform.position, transform.position);
                _InitialIPD = VR.Settings.IPDScale;
                _PrevFromTo = (OtherController.transform.position - transform.position).normalized;
                _ScaleInitialized = true;
            }
        }

        private void InitializeRotationIfNeeded()
        {
            if (!_ScaleInitialized && !_RotationInitialized)
            {
                _PrevFromTo = (OtherController.transform.position - transform.position).normalized;
                _RotationInitialized = true;
            }
        }
        private bool HasOtherLock()
        {
            return _OtherLock != null && _OtherLock.IsValid;
        }
    }
}
