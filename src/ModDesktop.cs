using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace ScamWYF.Modding.Core
{
    /// <summary>
    /// The computer the player is looking at right now: toast messages, opening an app without
    /// clicking its icon, and asking what is open.
    /// </summary>
    /// <remarks>
    /// This is for the session, not for content. Registering an app, a game or a scam is
    /// <see cref="Content.ContentRegistry"/>, which works whether or not the office scene is loaded;
    /// everything here needs the in-game desktop to exist, returns false when it does not, and is
    /// safe to call every frame from a mod that checks the return value.
    ///
    /// The game's own types are resolved by name and never referenced, for the same reason the
    /// content patches do it: a game update that renames one of them is a line in the log rather than
    /// a MissingMethodException in somebody's session. Costs one reflection walk per call, which is
    /// what makes these helpers fine at human rates and the wrong thing in a per-frame loop that
    /// ignores the result.
    /// </remarks>
    public static class ModDesktop
    {
        private const string FallbackIcon = "KolkataOS/SVG/notebook";
        private const string FallbackAccent = "#0078D7";

        private static readonly object Gate = new object();
        private static bool _reportedMissing;

        /// <summary>True when the computer is on screen and its controller exists.</summary>
        public static bool IsOpen
        {
            get { return Controller() != null; }
        }

        /// <summary>
        /// Put a message in the computer's notification stack, the same one the game uses for
        /// "new scams available". Returns false when the computer is not open, or when this build
        /// no longer has the notification centre where the game keeps it.
        /// </summary>
        public static bool Notify(ScamMod owner, string message)
        {
            return Notify(owner, message, FallbackIcon, FallbackAccent, null);
        }

        /// <summary>
        /// Put a message in the notification stack, with the game's own icon and accent colour.
        /// <paramref name="iconResource"/> is a game icon name such as
        /// <c>KolkataOS/SVG/phone-classic</c>; <paramref name="onClick"/> runs if the player clicks
        /// the notification, which is how the game's own notifications open the app they are about.
        /// </summary>
        public static bool Notify(ScamMod owner, string message, string iconResource,
            string accentColor, Action onClick)
        {
            if (string.IsNullOrEmpty(message)) return false;
            var controller = Controller();
            if (controller == null) return false;

            try
            {
                var centre = Field(controller, "m_notifications");
                if (centre == null) return false;

                var show = centre.GetType().GetMethod("ShowSvg",
                    BindingFlags.Public | BindingFlags.Instance);
                if (show == null)
                {
                    ReportMissing("KolkataOsNotificationCenter.ShowSvg");
                    return false;
                }

                // By name and not by signature: 0.1.91 added two optional parameters (richText,
                // slideIn) to a method that had five, so an exact-signature lookup found nothing and
                // every toast was silently refused. The first four are read for shape, the rest are
                // left at whatever defaults this build declares.
                var parameters = show.GetParameters();
                if (parameters.Length < 4 ||
                    parameters[0].ParameterType != typeof(string) ||
                    parameters[1].ParameterType != typeof(string) ||
                    parameters[2].ParameterType != typeof(string) ||
                    parameters[3].ParameterType != typeof(Action))
                {
                    ReportMissing("KolkataOsNotificationCenter.ShowSvg in the shape this expects");
                    return false;
                }

                var args = new object[parameters.Length];
                args[0] = message;
                args[1] = string.IsNullOrEmpty(iconResource) ? FallbackIcon : iconResource;
                args[2] = string.IsNullOrEmpty(accentColor) ? FallbackAccent : accentColor;
                args[3] = onClick;
                for (var i = 4; i < parameters.Length; i++)
                {
                    // Type.Missing lets Invoke supply this build's own default, which is what an
                    // optional parameter is for; a parameter that is not optional gets its type's
                    // default. DefaultValue itself is one of the members stripping took.
                    args[i] = parameters[i].IsOptional
                        ? Type.Missing
                        : DefaultOf(parameters[i].ParameterType);
                }

                show.Invoke(centre, args);
                return true;
            }
            catch (Exception ex)
            {
                Warn(owner, "Could not show a notification: " + Unwrap(ex).Message);
                return false;
            }
        }

        /// <summary>
        /// Open an app by id, as though the player had clicked it. Returns false when there is no
        /// such app in this session, or when the computer is not open.
        /// </summary>
        public static bool OpenApp(ScamMod owner, string appId)
        {
            if (string.IsNullOrEmpty(appId)) return false;
            var controller = Controller();
            if (controller == null) return false;

            try
            {
                var lookup = controller.GetType().GetMethod("GetAppDefinitionById",
                    BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null);
                if (lookup == null)
                {
                    ReportMissing("KolkataOsController.GetAppDefinitionById");
                    return false;
                }
                if (lookup.Invoke(controller, new object[] { appId }) == null)
                {
                    Warn(owner, "No app '" + appId + "' in this session, so it was not opened.");
                    return false;
                }

                var open = controller.GetType().GetMethod("OpenAppById",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance, null,
                    new[] { typeof(string) }, null);
                if (open == null)
                {
                    ReportMissing("KolkataOsController.OpenAppById");
                    return false;
                }
                open.Invoke(controller, new object[] { appId });
                return true;
            }
            catch (Exception ex)
            {
                Warn(owner, "Could not open app '" + appId + "': " + Unwrap(ex).Message);
                return false;
            }
        }

        /// <summary>True when that app's window is open. False without a running computer.</summary>
        public static bool IsAppOpen(string appId)
        {
            if (string.IsNullOrEmpty(appId)) return false;
            var controller = Controller();
            if (controller == null) return false;

            try
            {
                var method = controller.GetType().GetMethod("IsAppOpen",
                    BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null);
                if (method == null)
                {
                    ReportMissing("KolkataOsController.IsAppOpen");
                    return false;
                }
                return (bool)method.Invoke(controller, new object[] { appId });
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ---------------------------------------------------------------- internals

        /// <summary>The live controller, or null when the computer has never been opened.</summary>
        private static object Controller()
        {
            try
            {
                var singletonType = AccessTools.TypeByName("KolkataOsSingleton");
                if (singletonType == null)
                {
                    ReportMissing("KolkataOsSingleton");
                    return null;
                }

                var instance = singletonType.GetProperty("Instance",
                    BindingFlags.Public | BindingFlags.Static);
                if (instance == null)
                {
                    ReportMissing("KolkataOsSingleton.Instance");
                    return null;
                }

                var singleton = instance.GetValue(null, null);
                if (singleton == null) return null;

                var controller = singletonType.GetProperty("Controller",
                    BindingFlags.Public | BindingFlags.Instance);
                if (controller == null)
                {
                    ReportMissing("KolkataOsSingleton.Controller");
                    return null;
                }

                return controller.GetValue(singleton, null);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static object DefaultOf(Type type)
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        private static object Field(object instance, string name)
        {
            if (instance == null) return null;
            var field = instance.GetType().GetField(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return field != null ? field.GetValue(instance) : null;
        }

        private static Exception Unwrap(Exception ex)
        {
            return ex is TargetInvocationException && ex.InnerException != null
                ? ex.InnerException
                : ex;
        }

        private static void Warn(ScamMod owner, string message)
        {
            if (owner != null) owner.ModLog.LogWarning(message);
            else Log(LogLevel.Warning, message);
        }

        /// <summary>
        /// Say once, rather than every call, that something this build does not have is being asked
        /// for. A per-frame caller would otherwise write a warning per frame.
        /// </summary>
        private static void ReportMissing(string what)
        {
            lock (Gate)
            {
                if (_reportedMissing) return;
                _reportedMissing = true;
            }
            Log(LogLevel.Warning, "This build has no " + what + " (" + GameBuild.Describe() +
                                  "). The live desktop helpers are off.");
        }

        private static void Log(LogLevel level, string message)
        {
            try
            {
                Logger.CreateLogSource("ScamWYF.Modding").Log(level, message);
            }
            catch (Exception)
            {
                // Logging must never break a mod.
            }
        }
    }
}
