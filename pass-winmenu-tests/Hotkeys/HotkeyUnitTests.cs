﻿using System;
using System.Text;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows.Input;

namespace PassWinmenu.Hotkeys
{
    /// <summary>
    /// Unit tests for the <see cref="Hotkey"/> and related classes.
    /// </summary>
    [TestClass]
    public class HotkeyUnitTests
    {
        private const string Category = "Hotkeys: general";

        private const ModifierKeys Modifiers = ModifierKeys.Control;
        private const Key          KeyCode   = Key.A;
        private const bool         Repeats   = false;

        private Hotkey                    _hotkey;
        private DummyHotkeyRegistrar      _registrar;
        private readonly IHotkeyRegistrar _defaultRegistrar;

        public HotkeyUnitTests()
        {
            // Save initial value so we can reset to it between tests
            _defaultRegistrar = Hotkey.DefaultRegistrar;
        }


        // Executed before every test
        [TestInitialize]
        public void TestInit()
        {
            _registrar = new DummyHotkeyRegistrar();

            _hotkey = Hotkey.Register(Modifiers, KeyCode, Repeats)
                            .With(_registrar);

            Hotkey.DefaultRegistrar = _defaultRegistrar;
        }

        [TestMethod, TestCategory(Category)]
        public void DefaultRegistrar_FailsOnNull()
        {
            Assert.ThrowsException<ArgumentNullException>(
                () => Hotkey.DefaultRegistrar = null
                );
        }

        [TestMethod, TestCategory(Category)]
        public void Register_ReturnValue_ModifiersKeyRepeats()
        {
            var rr = Hotkey.Register(Modifiers, KeyCode, Repeats);

            Assert.IsTrue(rr is Hotkey.RegistrationRequest);

            Assert.AreEqual(Modifiers, rr.Modifiers);
            Assert.AreEqual(KeyCode, rr.Key);
            Assert.AreEqual(Repeats, rr.Repeats);
        }
        [TestMethod, TestCategory(Category)]
        public void Register_ReturnValue_KeyRepeats()
        {
            var rr = Hotkey.Register(KeyCode, Repeats);

            Assert.IsTrue(rr is Hotkey.RegistrationRequest);

            Assert.AreEqual(ModifierKeys.None, rr.Modifiers);
            Assert.AreEqual(KeyCode, rr.Key);
            Assert.AreEqual(Repeats, rr.Repeats);
        }

        [TestMethod, TestCategory(Category)]
        public void Register_DefaultRegistration()
        {
            Hotkey.DefaultRegistrar = _registrar;

            var hkCount = _registrar.Hotkeys.Count;

            Hotkey.RegistrationRequest rr = Hotkey.Register(KeyCode);

            Hotkey hk = rr;

            Assert.AreEqual(hkCount + 1, _registrar.Hotkeys.Count);
        }
        [TestMethod, TestCategory(Category)]
        public void Register_ExplicitRegistration()
        {
            var hkCount = _registrar.Hotkeys.Count;

            Hotkey hk = Hotkey.Register(KeyCode).With(_registrar);

            Assert.AreEqual(hkCount + 1, _registrar.Hotkeys.Count);
        }

        [TestMethod, TestCategory(Category)]
        public void Triggered_WhenEnabled()
        {
            _hotkey.Enabled = true;

            Assert.IsTrue(_hotkey.Enabled);

            bool fired = false;
            _hotkey.Triggered += (s, e) => fired = true;

            _registrar.Hotkeys[(Modifiers, KeyCode)](_registrar, null);

            Assert.IsTrue(fired);
        }
        [TestMethod, TestCategory(Category)]
        public void Triggered_WhenNotEnabled()
        {
            _hotkey.Enabled = false;

            Assert.IsFalse(_hotkey.Enabled);

            bool fired = false;
            _hotkey.Triggered += (s, e) => fired = true;

            _registrar.Hotkeys[(Modifiers, KeyCode)](_registrar, null);

            Assert.IsFalse(fired);
        }

        [TestMethod, TestCategory(Category)]
        public void Dispose_UnregistersWithRegistrar()
        {
            bool callsDispose = false;

            _registrar.Disposal += (s, combo) =>
            {
                Assert.AreEqual(Modifiers, combo.Item1);
                Assert.AreEqual(KeyCode, combo.Item2);

                callsDispose = true;
            };

            _hotkey.Dispose();

            Assert.IsTrue(callsDispose);
        }
        [TestMethod, TestCategory(Category)]
        public void Dispose_CanMakeMultipleCalls()
        {
            _hotkey.Dispose();
            _hotkey.Dispose();
            _hotkey.Dispose();
        }

        [TestMethod, TestCategory(Category)]
        public void Hotkey_DefaultPropertyValues()
        {
            Assert.AreEqual(true, _hotkey.Enabled);
            Assert.AreEqual(Modifiers, _hotkey.ModifierKeys);
            Assert.AreEqual(KeyCode, _hotkey.Key);
            Assert.AreEqual(Repeats, _hotkey.Repeats);
        }

    }
}
