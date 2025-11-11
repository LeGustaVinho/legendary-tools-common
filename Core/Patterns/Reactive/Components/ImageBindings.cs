using System;
using UnityEngine;
using UnityEngine.UI;
using LegendaryTools; // Observable<T>
using LegendaryTools.Reactive;

namespace LegendaryTools.Reactive.UGUI
{
    /// <summary>
    /// Extension methods to bind Observables with UnityEngine.UI.Image.
    /// Provides one-way bindings for Color, Sprite, Enabled, FillAmount and RaycastTarget.
    /// </summary>
    public static class ImageBindings
    {
        public static BindingHandle BindColor(
            this Image image,
            Observable<Color> colorObservable,
            MonoBehaviour owner = null,
            BindingOptions options = null)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            if (colorObservable == null) throw new ArgumentNullException(nameof(colorObservable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            options ??= new BindingOptions();

            void Apply(Color c)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                image.color = c;
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
                Kind = "Image.Color",
                Direction = "ToUI",
                Description = "image.color ← Observable<Color>",
                Target = image,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"color={image.color}"
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

        public static BindingHandle BindSprite(
            this Image image,
            Observable<Sprite> spriteObservable,
            MonoBehaviour owner = null,
            BindingOptions options = null,
            bool setNativeSizeOnChange = false)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            if (spriteObservable == null) throw new ArgumentNullException(nameof(spriteObservable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            options ??= new BindingOptions();

            void Apply(Sprite s)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                image.sprite = s;
                if (setNativeSizeOnChange && s != null) image.SetNativeSize();
            }

            void OnObsChanged(IObservable<Sprite> _, Sprite oldV, Sprite newV)
            {
                Apply(newV);
            }

            void Subscribe()
            {
                Apply(spriteObservable.Value);
                spriteObservable.OnChanged += OnObsChanged;
            }

            void Unsubscribe()
            {
                spriteObservable.OnChanged -= OnObsChanged;
            }

            void Resync()
            {
                Apply(spriteObservable.Value);
            }

            BindingInfo info = new()
            {
                Kind = "Image.Sprite",
                Direction = "ToUI",
                Description = "image.sprite ← Observable<Sprite>",
                Target = image,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"sprite={(image.sprite ? image.sprite.name : "<null>")}"
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
            this Image image,
            Observable<bool> enabledObservable,
            MonoBehaviour owner = null,
            BindingOptions options = null)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            if (enabledObservable == null) throw new ArgumentNullException(nameof(enabledObservable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            options ??= new BindingOptions();

            void Apply(bool v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                image.enabled = v;
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
                Kind = "Image.Enabled",
                Direction = "ToUI",
                Description = "image.enabled ← Observable<bool>",
                Target = image,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"enabled={image.enabled}"
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

        public static BindingHandle BindFillAmount(
            this Image image,
            Observable<float> fillObservable,
            MonoBehaviour owner = null,
            BindingOptions options = null,
            bool clamp01 = true)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            if (fillObservable == null) throw new ArgumentNullException(nameof(fillObservable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            options ??= new BindingOptions();

            void Apply(float v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                image.fillAmount = clamp01 ? Mathf.Clamp01(v) : v;
            }

            void OnObsChanged(IObservable<float> _, float oldV, float newV)
            {
                Apply(newV);
            }

            void Subscribe()
            {
                Apply(fillObservable.Value);
                fillObservable.OnChanged += OnObsChanged;
            }

            void Unsubscribe()
            {
                fillObservable.OnChanged -= OnObsChanged;
            }

            void Resync()
            {
                Apply(fillObservable.Value);
            }

            BindingInfo info = new()
            {
                Kind = "Image.FillAmount",
                Direction = "ToUI",
                Description = "image.fillAmount ← Observable<float>",
                Target = image,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"fillAmount={image.fillAmount:0.###}"
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
            this Image image,
            Observable<bool> raycastTargetObservable,
            MonoBehaviour owner = null,
            BindingOptions options = null)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            if (raycastTargetObservable == null) throw new ArgumentNullException(nameof(raycastTargetObservable));

            BindingAnchor anchor = owner != null
                ? owner.GetComponent<BindingAnchor>() ?? owner.gameObject.AddComponent<BindingAnchor>()
                : null;

            options ??= new BindingOptions();

            void Apply(bool v)
            {
                if (anchor != null && !anchor.ShouldProcessNow()) return;
                image.raycastTarget = v;
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
                Kind = "Image.RaycastTarget",
                Direction = "ToUI",
                Description = "image.raycastTarget ← Observable<bool>",
                Target = image,
                Owner = owner,
                Anchor = anchor,
                Options = options,
                GetState = () => $"raycastTarget={image.raycastTarget}"
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