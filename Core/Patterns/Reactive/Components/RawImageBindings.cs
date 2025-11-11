using System;
using UnityEngine;
using UnityEngine.UI;
using LegendaryTools; // Observable<T>
using LegendaryTools.Reactive;

namespace LegendaryTools.Reactive.UGUI
{
    /// <summary>
    /// Extension methods to bind Observables with UnityEngine.UI.RawImage.
    /// Provides one-way bindings for Color, Texture, UVRect, Enabled and RaycastTarget.
    /// </summary>
    public static class RawImageBindings
    {
        public static BindingHandle BindColor(
            this RawImage rawImage,
            Observable<Color> colorObservable,
            MonoBehaviour owner = null,
            BindingOptions options = null)
        {
            if (rawImage == null) throw new ArgumentNullException(nameof(rawImage));
            if (colorObservable == null) throw new ArgumentNullException(nameof(colorObservable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;
            options ??= new BindingOptions();

            void Apply(Color c)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                rawImage.color = c;
            }

            void OnObsChanged(IObservable<Color> _, Color oldV, Color newV)
            {
                Apply(newV);
            }

            void Subscribe()
            {
                Apply(colorObservable.Value);
                colorObservable.OnChanged += OnObsChanged;
            }

            void Unsubscribe()
            {
                colorObservable.OnChanged -= OnObsChanged;
            }

            void Resync()
            {
                Apply(colorObservable.Value);
            }

            BindingInfo info = new()
            {
                Kind = "RawImage.Color",
                Direction = "ToUI",
                Description = "rawImage.color ← Observable<Color>",
                Target = rawImage,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"color={rawImage.color}"
            };

            BindingHandle handle = new(Subscribe, Unsubscribe, Resync, anchor, options, info);
            if (owner != null)
            {
                BindingDisposer disposer = owner.GetComponent<BindingDisposer>() ??
                                           owner.gameObject.AddComponent<BindingDisposer>();
                disposer.Add(handle);
            }

            return handle;
        }

        public static BindingHandle BindTexture(
            this RawImage rawImage,
            Observable<Texture> textureObservable,
            MonoBehaviour owner = null,
            BindingOptions options = null,
            bool setNativeSizeOnChange = false)
        {
            if (rawImage == null) throw new ArgumentNullException(nameof(rawImage));
            if (textureObservable == null) throw new ArgumentNullException(nameof(textureObservable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;
            options ??= new BindingOptions();

            void Apply(Texture tex)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                rawImage.texture = tex;
                if (setNativeSizeOnChange && tex != null) rawImage.SetNativeSize();
            }

            void OnObsChanged(IObservable<Texture> _, Texture oldV, Texture newV)
            {
                Apply(newV);
            }

            void Subscribe()
            {
                Apply(textureObservable.Value);
                textureObservable.OnChanged += OnObsChanged;
            }

            void Unsubscribe()
            {
                textureObservable.OnChanged -= OnObsChanged;
            }

            void Resync()
            {
                Apply(textureObservable.Value);
            }

            BindingInfo info = new()
            {
                Kind = "RawImage.Texture",
                Direction = "ToUI",
                Description = "rawImage.texture ← Observable<Texture>",
                Target = rawImage,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"texture={(rawImage.texture ? rawImage.texture.name : "<null>")}"
            };

            BindingHandle handle = new(Subscribe, Unsubscribe, Resync, anchor, options, info);
            if (owner != null)
            {
                BindingDisposer disposer = owner.GetComponent<BindingDisposer>() ??
                                           owner.gameObject.AddComponent<BindingDisposer>();
                disposer.Add(handle);
            }

            return handle;
        }

        public static BindingHandle BindUVRect(
            this RawImage rawImage,
            Observable<Rect> uvRectObservable,
            MonoBehaviour owner = null,
            BindingOptions options = null)
        {
            if (rawImage == null) throw new ArgumentNullException(nameof(rawImage));
            if (uvRectObservable == null) throw new ArgumentNullException(nameof(uvRectObservable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;
            options ??= new BindingOptions();

            void Apply(Rect r)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                rawImage.uvRect = r;
            }

            void OnObsChanged(IObservable<Rect> _, Rect oldV, Rect newV)
            {
                Apply(newV);
            }

            void Subscribe()
            {
                Apply(uvRectObservable.Value);
                uvRectObservable.OnChanged += OnObsChanged;
            }

            void Unsubscribe()
            {
                uvRectObservable.OnChanged -= OnObsChanged;
            }

            void Resync()
            {
                Apply(uvRectObservable.Value);
            }

            BindingInfo info = new()
            {
                Kind = "RawImage.UVRect",
                Direction = "ToUI",
                Description = "rawImage.uvRect ← Observable<Rect>",
                Target = rawImage,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"uvRect={rawImage.uvRect}"
            };

            BindingHandle handle = new(Subscribe, Unsubscribe, Resync, anchor, options, info);
            if (owner != null)
            {
                BindingDisposer disposer = owner.GetComponent<BindingDisposer>() ??
                                           owner.gameObject.AddComponent<BindingDisposer>();
                disposer.Add(handle);
            }

            return handle;
        }

        public static BindingHandle BindEnabled(
            this RawImage rawImage,
            Observable<bool> enabledObservable,
            MonoBehaviour owner = null,
            BindingOptions options = null)
        {
            if (rawImage == null) throw new ArgumentNullException(nameof(rawImage));
            if (enabledObservable == null) throw new ArgumentNullException(nameof(enabledObservable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;
            options ??= new BindingOptions();

            void Apply(bool v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                rawImage.enabled = v;
            }

            void OnObsChanged(IObservable<bool> _, bool oldV, bool newV)
            {
                Apply(newV);
            }

            void Subscribe()
            {
                Apply(enabledObservable.Value);
                enabledObservable.OnChanged += OnObsChanged;
            }

            void Unsubscribe()
            {
                enabledObservable.OnChanged -= OnObsChanged;
            }

            void Resync()
            {
                Apply(enabledObservable.Value);
            }

            BindingInfo info = new()
            {
                Kind = "RawImage.Enabled",
                Direction = "ToUI",
                Description = "rawImage.enabled ← Observable<bool>",
                Target = rawImage,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"enabled={rawImage.enabled}"
            };

            BindingHandle handle = new(Subscribe, Unsubscribe, Resync, anchor, options, info);
            if (owner != null)
            {
                BindingDisposer disposer = owner.GetComponent<BindingDisposer>() ??
                                           owner.gameObject.AddComponent<BindingDisposer>();
                disposer.Add(handle);
            }

            return handle;
        }

        public static BindingHandle BindRaycastTarget(
            this RawImage rawImage,
            Observable<bool> raycastTargetObservable,
            MonoBehaviour owner = null,
            BindingOptions options = null)
        {
            if (rawImage == null) throw new ArgumentNullException(nameof(rawImage));
            if (raycastTargetObservable == null) throw new ArgumentNullException(nameof(raycastTargetObservable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;
            options ??= new BindingOptions();

            void Apply(bool v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                rawImage.raycastTarget = v;
            }

            void OnObsChanged(IObservable<bool> _, bool oldV, bool newV)
            {
                Apply(newV);
            }

            void Subscribe()
            {
                Apply(raycastTargetObservable.Value);
                raycastTargetObservable.OnChanged += OnObsChanged;
            }

            void Unsubscribe()
            {
                raycastTargetObservable.OnChanged -= OnObsChanged;
            }

            void Resync()
            {
                Apply(raycastTargetObservable.Value);
            }

            BindingInfo info = new()
            {
                Kind = "RawImage.RaycastTarget",
                Direction = "ToUI",
                Description = "rawImage.raycastTarget ← Observable<bool>",
                Target = rawImage,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"raycastTarget={rawImage.raycastTarget}"
            };

            BindingHandle handle = new(Subscribe, Unsubscribe, Resync, anchor, options, info);
            if (owner != null)
            {
                BindingDisposer disposer = owner.GetComponent<BindingDisposer>() ??
                                           owner.gameObject.AddComponent<BindingDisposer>();
                disposer.Add(handle);
            }

            return handle;
        }
    }
}