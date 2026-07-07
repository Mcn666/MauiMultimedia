(function(){
  if (typeof Object.hasOwn !== 'function') Object.hasOwn = function(o,p){ return Object.prototype.hasOwnProperty.call(o,p); };
  if (typeof Promise.withResolvers !== 'function') Promise.withResolvers = function(){ var r,a,j,p=new Promise(function(res,rej){a=res;j=rej;}); return {promise:p,resolve:a,reject:j}; };
  if (typeof Array.prototype.at !== 'function') Array.prototype.at = function(n){ n = Math.trunc(n) || 0; if (n < 0) n += this.length; return n < 0 || n >= this.length ? undefined : this[n]; };
  if (typeof FinalizationRegistry === 'undefined') { window.FinalizationRegistry = function(){ this.register=function(){}; this.unregister=function(){}; }; }
  if (typeof WeakRef === 'undefined') { window.WeakRef = function(o){ this.deref=function(){ return o; }; }; }
})();
(() => {
  !function(e) {
    "use strict";
    var t, n, r;
    !function(e2) {
      const t2 = [], n2 = "__jsObjectId", r2 = "__dotNetObject", o2 = "__byte[]", a2 = "__dotNetStream", s2 = "__jsStreamReferenceLength";
      let i2, c2, l2;
      class u2 {
        constructor(e3) {
          this._jsObject = e3, this._cachedHandlers = /* @__PURE__ */ new Map();
        }
        resolveInvocationHandler(e3, t3) {
          var n3;
          const r3 = null === (n3 = this._cachedHandlers.get(e3)) || void 0 === n3 ? void 0 : n3[t3];
          if (r3) return r3;
          const [o3, a3] = I2(this._jsObject, e3), s3 = function(e4, t4, n4, r4) {
            switch (n4) {
              case l2.FunctionCall:
                const n5 = e4[t4];
                if (n5 instanceof Function) return n5.bind(e4);
                throw new Error(`The value '${r4}' is not a function.`);
              case l2.ConstructorCall:
                const o4 = e4[t4];
                if (o4 instanceof Function) {
                  const t5 = o4.bind(e4);
                  return (...e5) => new t5(...e5);
                }
                throw new Error(`The value '${r4}' is not a function.`);
              case l2.GetValue:
                if (!function(e5, t5) {
                  if (!(t5 in e5)) return false;
                  for (; void 0 !== e5; ) {
                    const n6 = Object.getOwnPropertyDescriptor(e5, t5);
                    if (n6) return !!n6.hasOwnProperty("value") || n6.hasOwnProperty("get") && "function" == typeof n6.get;
                    e5 = Object.getPrototypeOf(e5);
                  }
                  return false;
                }(e4, t4)) throw new Error(`The property '${r4}' is not defined or is not readable.`);
                return () => e4[t4];
              case l2.SetValue:
                if (!function(e5, t5) {
                  if (!(t5 in e5)) return Object.isExtensible(e5);
                  for (; void 0 !== e5; ) {
                    const n6 = Object.getOwnPropertyDescriptor(e5, t5);
                    if (n6) return !(!n6.hasOwnProperty("value") || !n6.writable) || n6.hasOwnProperty("set") && "function" == typeof n6.set;
                    e5 = Object.getPrototypeOf(e5);
                  }
                  return false;
                }(e4, t4)) throw new Error(`The property '${r4}' is not writable.`);
                return (...n6) => e4[t4] = n6[0];
            }
          }(o3, a3, t3, e3);
          return this.addHandlerToCache(e3, s3, t3), s3;
        }
        getWrappedObject() {
          return this._jsObject;
        }
        addHandlerToCache(e3, t3, n3) {
          const r3 = this._cachedHandlers.get(e3);
          r3 ? r3[n3] = t3 : this._cachedHandlers.set(e3, { [n3]: t3 });
        }
      }
      !function(e3) {
        e3[e3.FunctionCall = 1] = "FunctionCall", e3[e3.ConstructorCall = 2] = "ConstructorCall", e3[e3.GetValue = 3] = "GetValue", e3[e3.SetValue = 4] = "SetValue";
      }(l2 = e2.JSCallType || (e2.JSCallType = {}));
      const d2 = 0, h2 = { [d2]: new u2(window) };
      h2[0]._cachedHandlers.set("import", { [l2.FunctionCall]: (e3) => ("string" == typeof e3 && e3.startsWith("./") && (e3 = new URL(e3.substring(2), document.baseURI).toString()), import(e3)) });
      let f2, p2 = 1;
      function m2(e3) {
        t2.push(e3);
      }
      function b2(e3) {
        if (null == e3) return { [n2]: -1 };
        if (e3 && ("object" == typeof e3 || e3 instanceof Function)) {
          h2[p2] = new u2(e3);
          const t3 = { [n2]: p2 };
          return p2++, t3;
        }
        throw new Error(`Cannot create a JSObjectReference from the value '${e3}'.`);
      }
      function v2(e3) {
        let t3 = -1;
        if (e3 instanceof ArrayBuffer && (e3 = new Uint8Array(e3)), e3 instanceof Blob) t3 = e3.size;
        else {
          if (!(e3.buffer instanceof ArrayBuffer)) throw new Error("Supplied value is not a typed array or blob.");
          if (void 0 === e3.byteLength) throw new Error(`Cannot create a JSStreamReference from the value '${e3}' as it doesn't have a byteLength.`);
          t3 = e3.byteLength;
        }
        const r3 = { [s2]: t3 };
        try {
          const t4 = b2(e3);
          r3[n2] = t4[n2];
        } catch (t4) {
          throw new Error(`Cannot create a JSStreamReference from the value '${e3}'.`);
        }
        return r3;
      }
      function g2(e3, n3) {
        c2 = e3;
        const r3 = n3 ? JSON.parse(n3, (e4, n4) => t2.reduce((t3, n5) => n5(e4, t3), n4)) : null;
        return c2 = void 0, r3;
      }
      function y2() {
        if (void 0 === i2) throw new Error("No call dispatcher has been set.");
        if (null === i2) throw new Error("There are multiple .NET runtimes present, so a default dispatcher could not be resolved. Use DotNetObject to invoke .NET instance methods.");
        return i2;
      }
      e2.attachDispatcher = function(e3) {
        const t3 = new w2(e3);
        return void 0 === i2 ? i2 = t3 : i2 && (i2 = null), t3;
      }, e2.attachReviver = m2, e2.invokeMethod = function(e3, t3, ...n3) {
        return y2().invokeDotNetStaticMethod(e3, t3, ...n3);
      }, e2.invokeMethodAsync = function(e3, t3, ...n3) {
        return y2().invokeDotNetStaticMethodAsync(e3, t3, ...n3);
      }, e2.createJSObjectReference = b2, e2.createJSStreamReference = v2, e2.disposeJSObjectReference = function(e3) {
        const t3 = e3 && e3[n2];
        "number" == typeof t3 && -1 !== t3 && C2(t3);
      }, function(e3) {
        e3[e3.Default = 0] = "Default", e3[e3.JSObjectReference = 1] = "JSObjectReference", e3[e3.JSStreamReference = 2] = "JSStreamReference", e3[e3.JSVoidResult = 3] = "JSVoidResult";
      }(f2 = e2.JSCallResultType || (e2.JSCallResultType = {}));
      class w2 {
        constructor(e3) {
          this._dotNetCallDispatcher = e3, this._byteArraysToBeRevived = /* @__PURE__ */ new Map(), this._pendingDotNetToJSStreams = /* @__PURE__ */ new Map(), this._pendingAsyncCalls = {}, this._nextAsyncCallId = 1;
        }
        getDotNetCallDispatcher() {
          return this._dotNetCallDispatcher;
        }
        invokeJSFromDotNet(e3, t3, n3, r3, o3) {
          const a3 = R2(this.processJSCall(r3, e3, o3, t3), n3);
          return null == a3 ? null : k2(this, a3);
        }
        async beginInvokeJSFromDotNet(e3, t3, n3, r3, o3, a3) {
          try {
            const s3 = this.processJSCall(o3, t3, a3, n3);
            if (e3) {
              const t4 = k2(this, [e3, true, R2(await s3, r3)]);
              this._dotNetCallDispatcher.endInvokeJSFromDotNet(e3, true, t4);
            }
          } catch (t4) {
            if (e3) {
              const n4 = JSON.stringify([e3, false, E2(t4)]);
              this._dotNetCallDispatcher.endInvokeJSFromDotNet(e3, false, n4);
            }
          }
        }
        processJSCall(e3, t3, n3, r3) {
          var o3;
          const a3 = null !== (o3 = g2(this, r3)) && void 0 !== o3 ? o3 : [];
          return S2(t3, e3, n3)(...a3);
        }
        endInvokeDotNetFromJS(e3, t3, n3) {
          const r3 = t3 ? g2(this, n3) : new Error(n3);
          this.completePendingCall(parseInt(e3, 10), t3, r3);
        }
        invokeDotNetStaticMethod(e3, t3, ...n3) {
          return this.invokeDotNetMethod(e3, t3, null, n3);
        }
        invokeDotNetStaticMethodAsync(e3, t3, ...n3) {
          return this.invokeDotNetMethodAsync(e3, t3, null, n3);
        }
        invokeDotNetMethod(e3, t3, n3, r3) {
          if (this._dotNetCallDispatcher.invokeDotNetFromJS) {
            const o3 = k2(this, r3), a3 = this._dotNetCallDispatcher.invokeDotNetFromJS(e3, t3, n3, o3);
            return a3 ? g2(this, a3) : null;
          }
          throw new Error("The current dispatcher does not support synchronous calls from JS to .NET. Use invokeDotNetMethodAsync instead.");
        }
        invokeDotNetMethodAsync(e3, t3, n3, r3) {
          if (e3 && n3) throw new Error(`For instance method calls, assemblyName should be null. Received '${e3}'.`);
          const o3 = this._nextAsyncCallId++, a3 = new Promise((e4, t4) => {
            this._pendingAsyncCalls[o3] = { resolve: e4, reject: t4 };
          });
          try {
            const a4 = k2(this, r3);
            this._dotNetCallDispatcher.beginInvokeDotNetFromJS(o3, e3, t3, n3, a4);
          } catch (e4) {
            this.completePendingCall(o3, false, e4);
          }
          return a3;
        }
        receiveByteArray(e3, t3) {
          this._byteArraysToBeRevived.set(e3, t3);
        }
        processByteArray(e3) {
          const t3 = this._byteArraysToBeRevived.get(e3);
          return t3 ? (this._byteArraysToBeRevived.delete(e3), t3) : null;
        }
        supplyDotNetStream(e3, t3) {
          if (this._pendingDotNetToJSStreams.has(e3)) {
            const n3 = this._pendingDotNetToJSStreams.get(e3);
            this._pendingDotNetToJSStreams.delete(e3), n3.resolve(t3);
          } else {
            const n3 = new N2();
            n3.resolve(t3), this._pendingDotNetToJSStreams.set(e3, n3);
          }
        }
        getDotNetStreamPromise(e3) {
          let t3;
          if (this._pendingDotNetToJSStreams.has(e3)) t3 = this._pendingDotNetToJSStreams.get(e3).streamPromise, this._pendingDotNetToJSStreams.delete(e3);
          else {
            const n3 = new N2();
            this._pendingDotNetToJSStreams.set(e3, n3), t3 = n3.streamPromise;
          }
          return t3;
        }
        completePendingCall(e3, t3, n3) {
          if (!this._pendingAsyncCalls.hasOwnProperty(e3)) throw new Error(`There is no pending async call with ID ${e3}.`);
          const r3 = this._pendingAsyncCalls[e3];
          delete this._pendingAsyncCalls[e3], t3 ? r3.resolve(n3) : r3.reject(n3);
        }
      }
      function E2(e3) {
        return e3 instanceof Error ? `${e3.message}
${e3.stack}` : e3 ? e3.toString() : "null";
      }
      function S2(e3, t3, n3) {
        const r3 = h2[t3];
        if (r3) return r3.resolveInvocationHandler(e3, null != n3 ? n3 : l2.FunctionCall);
        throw new Error(`JS object instance with ID ${t3} does not exist (has it been disposed?).`);
      }
      function C2(e3) {
        delete h2[e3];
      }
      function I2(e3, t3) {
        const n3 = t3.split(".");
        let r3 = e3;
        for (let e4 = 0; e4 < n3.length - 1; e4++) {
          const o3 = n3[e4];
          if (!r3 || "object" != typeof r3 || !(o3 in r3)) throw new Error(`Could not find '${t3}' ('${o3}' was undefined).`);
          r3 = r3[o3];
        }
        return [r3, n3[n3.length - 1]];
      }
      e2.findJSFunction = S2, e2.disposeJSObjectReferenceById = C2, e2.findObjectMember = I2;
      class D2 {
        constructor(e3, t3) {
          this._id = e3, this._callDispatcher = t3;
        }
        invokeMethod(e3, ...t3) {
          return this._callDispatcher.invokeDotNetMethod(null, e3, this._id, t3);
        }
        invokeMethodAsync(e3, ...t3) {
          return this._callDispatcher.invokeDotNetMethodAsync(null, e3, this._id, t3);
        }
        dispose() {
          this._callDispatcher.invokeDotNetMethodAsync(null, "__Dispose", this._id, null).catch((e3) => console.error(e3));
        }
        serializeAsArg() {
          return { [r2]: this._id };
        }
      }
      e2.DotNetObject = D2, m2(function(e3, t3) {
        if (t3 && "object" == typeof t3) {
          if (t3.hasOwnProperty(r2)) return new D2(t3[r2], c2);
          if (t3.hasOwnProperty(n2)) {
            const e4 = t3[n2], r3 = h2[e4];
            if (r3) return r3.getWrappedObject();
            throw new Error(`JS object instance with Id '${e4}' does not exist. It may have been disposed.`);
          }
          if (t3.hasOwnProperty(o2)) {
            const e4 = t3[o2], n3 = c2.processByteArray(e4);
            if (void 0 === n3) throw new Error(`Byte array index '${e4}' does not exist.`);
            return n3;
          }
          if (t3.hasOwnProperty(a2)) {
            const e4 = t3[a2], n3 = c2.getDotNetStreamPromise(e4);
            return new A2(n3);
          }
        }
        return t3;
      });
      class A2 {
        constructor(e3) {
          this._streamPromise = e3;
        }
        stream() {
          return this._streamPromise;
        }
        async arrayBuffer() {
          return new Response(await this.stream()).arrayBuffer();
        }
      }
      class N2 {
        constructor() {
          this.streamPromise = new Promise((e3, t3) => {
            this.resolve = e3, this.reject = t3;
          });
        }
      }
      function R2(e3, t3) {
        switch (t3) {
          case f2.Default:
            return e3;
          case f2.JSObjectReference:
            return b2(e3);
          case f2.JSStreamReference:
            return v2(e3);
          case f2.JSVoidResult:
            return null;
          default:
            throw new Error(`Invalid JS call result type '${t3}'.`);
        }
      }
      let T2 = 0;
      function k2(e3, t3) {
        T2 = 0, c2 = e3;
        const n3 = JSON.stringify(t3, O2);
        return c2 = void 0, n3;
      }
      function O2(e3, t3) {
        if (t3 instanceof D2) return t3.serializeAsArg();
        if (t3 instanceof Uint8Array) {
          c2.getDotNetCallDispatcher().sendByteArray(T2, t3);
          const e4 = { [o2]: T2 };
          return T2++, e4;
        }
        return t3;
      }
    }(t || (t = {})), function(e2) {
      e2[e2.prependFrame = 1] = "prependFrame", e2[e2.removeFrame = 2] = "removeFrame", e2[e2.setAttribute = 3] = "setAttribute", e2[e2.removeAttribute = 4] = "removeAttribute", e2[e2.updateText = 5] = "updateText", e2[e2.stepIn = 6] = "stepIn", e2[e2.stepOut = 7] = "stepOut", e2[e2.updateMarkup = 8] = "updateMarkup", e2[e2.permutationListEntry = 9] = "permutationListEntry", e2[e2.permutationListEnd = 10] = "permutationListEnd";
    }(n || (n = {})), function(e2) {
      e2[e2.element = 1] = "element", e2[e2.text = 2] = "text", e2[e2.attribute = 3] = "attribute", e2[e2.component = 4] = "component", e2[e2.region = 5] = "region", e2[e2.elementReferenceCapture = 6] = "elementReferenceCapture", e2[e2.markup = 8] = "markup", e2[e2.namedEvent = 10] = "namedEvent";
    }(r || (r = {}));
    class o {
      constructor(e2, t2) {
        this.componentId = e2, this.fieldValue = t2;
      }
      static fromEvent(e2, t2) {
        const n2 = t2.target;
        if (n2 instanceof Element) {
          const t3 = function(e3) {
            return e3 instanceof HTMLInputElement ? e3.type && "checkbox" === e3.type.toLowerCase() ? { value: e3.checked } : { value: e3.value } : e3 instanceof HTMLSelectElement || e3 instanceof HTMLTextAreaElement ? { value: e3.value } : null;
          }(n2);
          if (t3) return new o(e2, t3.value);
        }
        return null;
      }
    }
    const a = /* @__PURE__ */ new Map(), s = /* @__PURE__ */ new Map(), i = [];
    function c(e2) {
      return a.get(e2);
    }
    function l(e2) {
      const t2 = a.get(e2);
      return (t2 == null ? void 0 : t2.browserEventName) || e2;
    }
    function u(e2, t2) {
      e2.forEach((e3) => a.set(e3, t2));
    }
    function d(e2) {
      const t2 = [];
      for (let n2 = 0; n2 < e2.length; n2++) {
        const r2 = e2[n2];
        t2.push({ identifier: r2.identifier, clientX: r2.clientX, clientY: r2.clientY, screenX: r2.screenX, screenY: r2.screenY, pageX: r2.pageX, pageY: r2.pageY });
      }
      return t2;
    }
    function h(e2) {
      return { detail: e2.detail, screenX: e2.screenX, screenY: e2.screenY, clientX: e2.clientX, clientY: e2.clientY, offsetX: e2.offsetX, offsetY: e2.offsetY, pageX: e2.pageX, pageY: e2.pageY, movementX: e2.movementX, movementY: e2.movementY, button: e2.button, buttons: e2.buttons, ctrlKey: e2.ctrlKey, shiftKey: e2.shiftKey, altKey: e2.altKey, metaKey: e2.metaKey, type: e2.type };
    }
    u(["input", "change"], { createEventArgs: function(e2) {
      const t2 = e2.target;
      if (function(e3) {
        return -1 !== f.indexOf(e3.getAttribute("type"));
      }(t2)) {
        const e3 = function(e4) {
          const t3 = e4.value, n2 = e4.type;
          switch (n2) {
            case "date":
            case "month":
            case "week":
              return t3;
            case "datetime-local":
              return 16 === t3.length ? t3 + ":00" : t3;
            case "time":
              return 5 === t3.length ? t3 + ":00" : t3;
          }
          throw new Error(`Invalid element type '${n2}'.`);
        }(t2);
        return { value: e3 };
      }
      if (function(e3) {
        return e3 instanceof HTMLSelectElement && "select-multiple" === e3.type;
      }(t2)) {
        const e3 = t2;
        return { value: Array.from(e3.options).filter((e4) => e4.selected).map((e4) => e4.value) };
      }
      {
        const e3 = function(e4) {
          return !!e4 && "INPUT" === e4.tagName && "checkbox" === e4.getAttribute("type");
        }(t2);
        return { value: e3 ? !!t2.checked : t2.value };
      }
    } }), u(["copy", "cut", "paste"], { createEventArgs: (e2) => ({ type: e2.type }) }), u(["drag", "dragend", "dragenter", "dragleave", "dragover", "dragstart", "drop"], { createEventArgs: (e2) => {
      return { ...h(t2 = e2), dataTransfer: t2.dataTransfer ? { dropEffect: t2.dataTransfer.dropEffect, effectAllowed: t2.dataTransfer.effectAllowed, files: Array.from(t2.dataTransfer.files).map((e3) => e3.name), items: Array.from(t2.dataTransfer.items).map((e3) => ({ kind: e3.kind, type: e3.type })), types: t2.dataTransfer.types } : null };
      var t2;
    } }), u(["focus", "blur", "focusin", "focusout"], { createEventArgs: (e2) => ({ type: e2.type }) }), u(["keydown", "keyup", "keypress"], { createEventArgs: (e2) => {
      return { key: (t2 = e2).key, code: t2.code, location: t2.location, repeat: t2.repeat, ctrlKey: t2.ctrlKey, shiftKey: t2.shiftKey, altKey: t2.altKey, metaKey: t2.metaKey, type: t2.type, isComposing: t2.isComposing };
      var t2;
    } }), u(["contextmenu", "click", "mouseover", "mouseout", "mousemove", "mousedown", "mouseup", "mouseleave", "mouseenter", "dblclick"], { createEventArgs: (e2) => h(e2) }), u(["error"], { createEventArgs: (e2) => {
      return { message: (t2 = e2).message, filename: t2.filename, lineno: t2.lineno, colno: t2.colno, type: t2.type };
      var t2;
    } }), u(["loadstart", "timeout", "abort", "load", "loadend", "progress"], { createEventArgs: (e2) => {
      return { lengthComputable: (t2 = e2).lengthComputable, loaded: t2.loaded, total: t2.total, type: t2.type };
      var t2;
    } }), u(["touchcancel", "touchend", "touchmove", "touchenter", "touchleave", "touchstart"], { createEventArgs: (e2) => {
      return { detail: (t2 = e2).detail, touches: d(t2.touches), targetTouches: d(t2.targetTouches), changedTouches: d(t2.changedTouches), ctrlKey: t2.ctrlKey, shiftKey: t2.shiftKey, altKey: t2.altKey, metaKey: t2.metaKey, type: t2.type };
      var t2;
    } }), u(["gotpointercapture", "lostpointercapture", "pointercancel", "pointerdown", "pointerenter", "pointerleave", "pointermove", "pointerout", "pointerover", "pointerup"], { createEventArgs: (e2) => {
      return { ...h(t2 = e2), pointerId: t2.pointerId, width: t2.width, height: t2.height, pressure: t2.pressure, tiltX: t2.tiltX, tiltY: t2.tiltY, pointerType: t2.pointerType, isPrimary: t2.isPrimary };
      var t2;
    } }), u(["wheel", "mousewheel"], { createEventArgs: (e2) => {
      return { ...h(t2 = e2), deltaX: t2.deltaX, deltaY: t2.deltaY, deltaZ: t2.deltaZ, deltaMode: t2.deltaMode };
      var t2;
    } }), u(["cancel", "close", "toggle"], { createEventArgs: () => ({}) });
    const f = ["date", "datetime-local", "month", "time", "week"], p = /* @__PURE__ */ new Map();
    let m, b, v = 0;
    const g = { async add(e2, t2, n2) {
      if (!n2) throw new Error("initialParameters must be an object, even if empty.");
      const r2 = "__bl-dynamic-root:" + (++v).toString();
      p.set(r2, e2);
      const o2 = await E().invokeMethodAsync("AddRootComponent", t2, r2), a2 = new w(o2, b[t2]);
      return await a2.setParameters(n2), a2;
    } };
    class y {
      invoke(e2) {
        return this._callback(e2);
      }
      setCallback(e2) {
        this._selfJSObjectReference || (this._selfJSObjectReference = t.createJSObjectReference(this)), this._callback = e2;
      }
      getJSObjectReference() {
        return this._selfJSObjectReference;
      }
      dispose() {
        this._selfJSObjectReference && t.disposeJSObjectReference(this._selfJSObjectReference);
      }
    }
    class w {
      constructor(e2, t2) {
        this._jsEventCallbackWrappers = /* @__PURE__ */ new Map(), this._componentId = e2;
        for (const e3 of t2) "eventcallback" === e3.type && this._jsEventCallbackWrappers.set(e3.name.toLowerCase(), new y());
      }
      setParameters(e2) {
        const t2 = {}, n2 = Object.entries(e2 || {}), r2 = n2.length;
        for (const [e3, r3] of n2) {
          const n3 = this._jsEventCallbackWrappers.get(e3.toLowerCase());
          n3 && r3 ? (n3.setCallback(r3), t2[e3] = n3.getJSObjectReference()) : t2[e3] = r3;
        }
        return E().invokeMethodAsync("SetRootComponentParameters", this._componentId, r2, t2);
      }
      async dispose() {
        if (null !== this._componentId) {
          await E().invokeMethodAsync("RemoveRootComponent", this._componentId), this._componentId = null;
          for (const e2 of this._jsEventCallbackWrappers.values()) e2.dispose();
        }
      }
    }
    function E() {
      if (!m) throw new Error("Dynamic root components have not been enabled in this application.");
      return m;
    }
    const S = /* @__PURE__ */ new Map(), C = [], I = /* @__PURE__ */ new Map();
    function D(e2) {
      return S.has(e2);
    }
    function A(e2, t2, n2) {
      return R(e2, t2.eventHandlerId, () => N(e2).invokeMethodAsync("DispatchEventAsync", t2, n2));
    }
    function N(e2) {
      const t2 = S.get(e2);
      if (!t2) throw new Error(`No interop methods are registered for renderer ${e2}`);
      return t2;
    }
    let R = (e2, t2, n2) => n2();
    const T = F(["abort", "blur", "cancel", "canplay", "canplaythrough", "change", "close", "cuechange", "durationchange", "emptied", "ended", "error", "focus", "load", "loadeddata", "loadedmetadata", "loadend", "loadstart", "mouseenter", "mouseleave", "pointerenter", "pointerleave", "pause", "play", "playing", "progress", "ratechange", "reset", "scroll", "seeked", "seeking", "stalled", "submit", "suspend", "timeupdate", "toggle", "unload", "volumechange", "waiting", "DOMNodeInsertedIntoDocument", "DOMNodeRemovedFromDocument"]), k = { submit: true }, O = F(["click", "dblclick", "mousedown", "mousemove", "mouseup"]);
    const __ = class __ {
      constructor(e2) {
        this.browserRendererId = e2, this.afterClickCallbacks = [];
        const t2 = ++__.nextEventDelegatorId;
        this.eventsCollectionKey = `_blazorEvents_${t2}`, this.eventInfoStore = new L(this.onGlobalEvent.bind(this));
      }
      setListener(e2, t2, n2, r2) {
        const o2 = this.getEventHandlerInfosForElement(e2, true), a2 = o2.getHandler(t2);
        if (a2) this.eventInfoStore.update(a2.eventHandlerId, n2);
        else {
          const a3 = { element: e2, eventName: t2, eventHandlerId: n2, renderingComponentId: r2 };
          this.eventInfoStore.add(a3), o2.setHandler(t2, a3);
        }
      }
      getHandler(e2) {
        return this.eventInfoStore.get(e2);
      }
      removeListener(e2) {
        const t2 = this.eventInfoStore.remove(e2);
        if (t2) {
          const e3 = t2.element, n2 = this.getEventHandlerInfosForElement(e3, false);
          n2 && n2.removeHandler(t2.eventName);
        }
      }
      removeListenersForElement(e2) {
        const t2 = this.getEventHandlerInfosForElement(e2, false);
        if (t2) {
          for (const e3 of t2.enumerateHandlers()) this.eventInfoStore.remove(e3.eventHandlerId);
          delete e2[this.eventsCollectionKey];
        }
      }
      notifyAfterClick(e2) {
        this.afterClickCallbacks.push(e2), this.eventInfoStore.addGlobalListener("click");
      }
      setStopPropagation(e2, t2, n2) {
        const r2 = this.getEventHandlerInfosForElement(e2, true), o2 = r2.stopPropagation(t2);
        r2.stopPropagation(t2, n2), !o2 && n2 ? this.eventInfoStore.addGlobalListener(t2) : o2 && !n2 && this.eventInfoStore.decrementCountByEventName(t2);
      }
      setPreventDefault(e2, t2, n2) {
        const r2 = this.getEventHandlerInfosForElement(e2, true), o2 = r2.preventDefault(t2);
        r2.preventDefault(t2, n2), !o2 && n2 ? this.eventInfoStore.addActiveGlobalListener(t2) : o2 && !n2 && this.eventInfoStore.decrementCountByEventName(t2);
      }
      onGlobalEvent(e2) {
        if (!(e2.target instanceof Element)) return;
        if (!D(this.browserRendererId)) return;
        this.dispatchGlobalEventToAllElements(e2.type, e2);
        const t2 = (n2 = e2.type, s.get(n2));
        var n2;
        t2 && t2.forEach((t3) => this.dispatchGlobalEventToAllElements(t3, e2)), "click" === e2.type && this.afterClickCallbacks.forEach((t3) => t3(e2));
      }
      dispatchGlobalEventToAllElements(e2, t2) {
        const n2 = t2.composedPath();
        let r2 = n2.shift(), a2 = null, s2 = false;
        const i2 = Object.prototype.hasOwnProperty.call(T, e2);
        let l2 = false;
        for (; r2; ) {
          const h2 = r2, f2 = this.getEventHandlerInfosForElement(h2, false);
          if (f2) {
            const n3 = f2.getHandler(e2);
            if (n3 && (u2 = h2, d2 = t2.type, !((u2 instanceof HTMLButtonElement || u2 instanceof HTMLInputElement || u2 instanceof HTMLTextAreaElement || u2 instanceof HTMLSelectElement) && Object.prototype.hasOwnProperty.call(O, d2) && u2.disabled))) {
              if (!s2) {
                const n4 = c(e2);
                a2 = (n4 == null ? void 0 : n4.createEventArgs) ? n4.createEventArgs(t2) : {}, s2 = true;
              }
              Object.prototype.hasOwnProperty.call(k, t2.type) && t2.preventDefault(), A(this.browserRendererId, { eventHandlerId: n3.eventHandlerId, eventName: e2, eventFieldInfo: o.fromEvent(n3.renderingComponentId, t2) }, a2);
            }
            f2.stopPropagation(e2) && (l2 = true), f2.preventDefault(e2) && t2.preventDefault();
          }
          r2 = i2 || l2 ? void 0 : n2.shift();
        }
        var u2, d2;
      }
      getEventHandlerInfosForElement(e2, t2) {
        return Object.prototype.hasOwnProperty.call(e2, this.eventsCollectionKey) ? e2[this.eventsCollectionKey] : t2 ? e2[this.eventsCollectionKey] = new x() : null;
      }
    };
    __.nextEventDelegatorId = 0;
    let _ = __;
    class L {
      constructor(e2) {
        this.globalListener = e2, this.infosByEventHandlerId = {}, this.countByEventName = {}, i.push(this.handleEventNameAliasAdded.bind(this));
      }
      add(e2) {
        if (this.infosByEventHandlerId[e2.eventHandlerId]) throw new Error(`Event ${e2.eventHandlerId} is already tracked`);
        this.infosByEventHandlerId[e2.eventHandlerId] = e2, this.addGlobalListener(e2.eventName);
      }
      get(e2) {
        return this.infosByEventHandlerId[e2];
      }
      addGlobalListener(e2) {
        if (e2 = l(e2), Object.prototype.hasOwnProperty.call(this.countByEventName, e2)) this.countByEventName[e2]++;
        else {
          this.countByEventName[e2] = 1;
          const t2 = Object.prototype.hasOwnProperty.call(T, e2);
          document.addEventListener(e2, this.globalListener, t2);
        }
      }
      addActiveGlobalListener(e2) {
        e2 = l(e2), Object.prototype.hasOwnProperty.call(this.countByEventName, e2) ? (this.countByEventName[e2]++, document.removeEventListener(e2, this.globalListener)) : this.countByEventName[e2] = 1;
        const t2 = Object.prototype.hasOwnProperty.call(T, e2);
        document.addEventListener(e2, this.globalListener, { capture: t2, passive: false });
      }
      update(e2, t2) {
        if (Object.prototype.hasOwnProperty.call(this.infosByEventHandlerId, t2)) throw new Error(`Event ${t2} is already tracked`);
        const n2 = this.infosByEventHandlerId[e2];
        delete this.infosByEventHandlerId[e2], n2.eventHandlerId = t2, this.infosByEventHandlerId[t2] = n2;
      }
      remove(e2) {
        const t2 = this.infosByEventHandlerId[e2];
        if (t2) {
          delete this.infosByEventHandlerId[e2];
          const n2 = l(t2.eventName);
          this.decrementCountByEventName(n2);
        }
        return t2;
      }
      decrementCountByEventName(e2) {
        0 == --this.countByEventName[e2] && (delete this.countByEventName[e2], document.removeEventListener(e2, this.globalListener));
      }
      handleEventNameAliasAdded(e2, t2) {
        if (Object.prototype.hasOwnProperty.call(this.countByEventName, e2)) {
          const n2 = this.countByEventName[e2];
          delete this.countByEventName[e2], document.removeEventListener(e2, this.globalListener), this.addGlobalListener(t2), this.countByEventName[t2] += n2 - 1;
        }
      }
    }
    class x {
      constructor() {
        this.handlers = {}, this.preventDefaultFlags = null, this.stopPropagationFlags = null;
      }
      *enumerateHandlers() {
        for (const e2 in this.handlers) Object.prototype.hasOwnProperty.call(this.handlers, e2) && (yield this.handlers[e2]);
      }
      getHandler(e2) {
        return Object.prototype.hasOwnProperty.call(this.handlers, e2) ? this.handlers[e2] : null;
      }
      setHandler(e2, t2) {
        this.handlers[e2] = t2;
      }
      removeHandler(e2) {
        delete this.handlers[e2];
      }
      preventDefault(e2, t2) {
        return void 0 !== t2 && (this.preventDefaultFlags = this.preventDefaultFlags || {}, this.preventDefaultFlags[e2] = t2), !!this.preventDefaultFlags && this.preventDefaultFlags[e2];
      }
      stopPropagation(e2, t2) {
        return void 0 !== t2 && (this.stopPropagationFlags = this.stopPropagationFlags || {}, this.stopPropagationFlags[e2] = t2), !!this.stopPropagationFlags && this.stopPropagationFlags[e2];
      }
    }
    function F(e2) {
      const t2 = {};
      return e2.forEach((e3) => {
        t2[e3] = true;
      }), t2;
    }
    const P = Symbol(), H = Symbol();
    function B(e2, t2) {
      if (P in e2) return e2;
      const n2 = [];
      if (e2.childNodes.length > 0) {
        if (!t2) throw new Error("New logical elements must start empty, or allowExistingContents must be true");
        e2.childNodes.forEach((t3) => {
          const r2 = B(t3, true);
          r2[H] = e2, n2.push(r2);
        });
      }
      return e2[P] = n2, e2;
    }
    function M(e2) {
      const t2 = K(e2);
      for (; t2.length; ) U(e2, 0);
    }
    function j(e2, t2) {
      const n2 = document.createComment("!");
      return J(n2, e2, t2), n2;
    }
    function J(e2, t2, n2) {
      const r2 = e2;
      let o2 = e2;
      if (e2 instanceof Comment) {
        const t3 = K(r2);
        if ((t3 == null ? void 0 : t3.length) > 0) {
          const t4 = Z(r2), n3 = new Range();
          n3.setStartBefore(e2), n3.setEndAfter(t4), o2 = n3.extractContents();
        }
      }
      const a2 = z(r2);
      if (a2) {
        const e3 = K(a2), t3 = Array.prototype.indexOf.call(e3, r2);
        e3.splice(t3, 1), delete r2[H];
      }
      const s2 = K(t2);
      if (n2 < s2.length) {
        const e3 = s2[n2];
        e3.parentNode.insertBefore(o2, e3), s2.splice(n2, 0, r2);
      } else q(o2, t2), s2.push(r2);
      r2[H] = t2, P in r2 || (r2[P] = []);
    }
    function U(e2, t2) {
      const n2 = K(e2).splice(t2, 1)[0];
      if (n2 instanceof Comment) {
        const e3 = K(n2);
        if (e3) for (; e3.length > 0; ) U(n2, 0);
      }
      const r2 = n2;
      r2.parentNode.removeChild(r2);
    }
    function z(e2) {
      return e2[H] || null;
    }
    function $(e2, t2) {
      return K(e2)[t2];
    }
    function W(e2) {
      const t2 = G(e2);
      return "http://www.w3.org/2000/svg" === t2.namespaceURI && "foreignObject" !== t2.tagName;
    }
    function K(e2) {
      return e2[P];
    }
    function V(e2) {
      const t2 = K(z(e2));
      return t2[Array.prototype.indexOf.call(t2, e2) + 1] || null;
    }
    function* X(e2) {
      const t2 = K(e2);
      for (const e3 of t2) yield* X(e3);
      yield e2;
    }
    function Y(e2, t2) {
      const n2 = K(e2);
      t2.forEach((e3) => {
        e3.moveRangeStart = n2[e3.fromSiblingIndex], e3.moveRangeEnd = Z(e3.moveRangeStart);
      }), t2.forEach((t3) => {
        const r2 = document.createComment("marker");
        t3.moveToBeforeMarker = r2;
        const o2 = n2[t3.toSiblingIndex + 1];
        o2 ? o2.parentNode.insertBefore(r2, o2) : q(r2, e2);
      }), t2.forEach((e3) => {
        const t3 = e3.moveToBeforeMarker, n3 = t3.parentNode, r2 = e3.moveRangeStart, o2 = e3.moveRangeEnd;
        let a2 = r2;
        for (; a2; ) {
          const e4 = a2.nextSibling;
          if (n3.insertBefore(a2, t3), a2 === o2) break;
          a2 = e4;
        }
        n3.removeChild(t3);
      }), t2.forEach((e3) => {
        n2[e3.toSiblingIndex] = e3.moveRangeStart;
      });
    }
    function G(e2) {
      if (e2 instanceof Element || e2 instanceof DocumentFragment) return e2;
      if (e2 instanceof Comment) return e2.parentNode;
      throw new Error("Not a valid logical element");
    }
    function q(e2, t2) {
      if (t2 instanceof Element || t2 instanceof DocumentFragment) t2.appendChild(e2);
      else {
        if (!(t2 instanceof Comment)) throw new Error(`Cannot append node because the parent is not a valid logical element. Parent: ${t2}`);
        {
          const n2 = V(t2);
          n2 ? n2.parentNode.insertBefore(e2, n2) : q(e2, z(t2));
        }
      }
    }
    function Z(e2) {
      if (e2 instanceof Element || e2 instanceof DocumentFragment) return e2;
      const t2 = V(e2);
      if (t2) return t2.previousSibling;
      {
        const t3 = z(e2);
        return t3 instanceof Element || t3 instanceof DocumentFragment ? t3.lastChild : Z(t3);
      }
    }
    function Q(e2) {
      return `_bl_${e2}`;
    }
    const ee = "__internalId";
    t.attachReviver((e2, t2) => t2 && "object" == typeof t2 && Object.prototype.hasOwnProperty.call(t2, ee) && "string" == typeof t2[ee] ? function(e3) {
      const t3 = `[${Q(e3)}]`;
      return document.querySelector(t3);
    }(t2[ee]) : t2);
    const te = "_blazorDeferredValue";
    function ne(e2) {
      return "select-multiple" === e2.type;
    }
    function re(e2, t2) {
      e2.value = t2 || "";
    }
    function oe(e2, t2) {
      e2 instanceof HTMLSelectElement ? ne(e2) ? function(e3, t3) {
        t3 || (t3 = []);
        for (let n2 = 0; n2 < e3.options.length; n2++) e3.options[n2].selected = -1 !== t3.indexOf(e3.options[n2].value);
      }(e2, t2) : re(e2, t2) : e2.value = t2;
    }
    function ae(e2) {
      const t2 = function(e3) {
        for (; e3; ) {
          if (e3 instanceof HTMLSelectElement) return e3;
          e3 = e3.parentElement;
        }
        return null;
      }(e2);
      if (!function(e3) {
        return !!e3 && te in e3;
      }(t2)) return false;
      if (ne(t2)) e2.selected = -1 !== t2._blazorDeferredValue.indexOf(e2.value);
      else {
        if (t2._blazorDeferredValue !== e2.value) return false;
        re(t2, e2.value), delete t2._blazorDeferredValue;
      }
      return true;
    }
    const se = document.createElement("template"), ie = document.createElementNS("http://www.w3.org/2000/svg", "g"), ce = /* @__PURE__ */ new Set(), le = Symbol(), ue = Symbol();
    class de {
      constructor(e2) {
        this.rootComponentIds = /* @__PURE__ */ new Set(), this.childComponentLocations = {}, this.eventDelegator = new _(e2), this.eventDelegator.notifyAfterClick((e3) => {
          Se() && function(e4) {
            if (0 !== e4.button || function(e5) {
              return e5.ctrlKey || e5.shiftKey || e5.altKey || e5.metaKey;
            }(e4)) return;
            if (e4.defaultPrevented) return;
            const t2 = function(e5) {
              const t3 = e5.composedPath && e5.composedPath();
              if (t3) for (let e6 = 0; e6 < t3.length; e6++) {
                const n2 = t3[e6];
                if (n2 instanceof HTMLAnchorElement || n2 instanceof SVGAElement) return n2;
              }
              return null;
            }(e4);
            if (t2 && function(e5) {
              const t3 = e5.getAttribute("target");
              return (!t3 || "_self" === t3) && e5.hasAttribute("href") && !e5.hasAttribute("download");
            }(t2)) {
              const n2 = Ee(t2.getAttribute("href"));
              ye(n2) && (e4.preventDefault(), _e(n2, true, false));
            }
          }(e3);
        });
      }
      getRootComponentCount() {
        return this.rootComponentIds.size;
      }
      attachRootComponentToLogicalElement(e2, t2, n2) {
        if (function(e3) {
          return e3[le];
        }(t2)) throw new Error(`Root component '${e2}' could not be attached because its target element is already associated with a root component`);
        n2 && (t2 = j(t2, K(t2).length)), he(t2, true), this.attachComponentToElement(e2, t2), this.rootComponentIds.add(e2), ce.add(t2);
      }
      updateComponent(e2, t2, n2, r2) {
        var _a;
        const o2 = this.childComponentLocations[t2];
        if (!o2) throw new Error(`No element is currently associated with component ${t2}`);
        ce.delete(o2) && (this.detachEventHandlersFromElement(o2), M(o2), o2 instanceof Comment && (o2.textContent = "!"));
        const a2 = (_a = G(o2)) == null ? void 0 : _a.getRootNode(), s2 = a2 && a2.activeElement;
        this.applyEdits(e2, t2, o2, 0, n2, r2), s2 instanceof HTMLElement && a2 && a2.activeElement !== s2 && s2.focus();
      }
      disposeComponent(e2) {
        if (this.rootComponentIds.delete(e2)) {
          const t2 = this.childComponentLocations[e2];
          he(t2, false), true === t2[ue] ? ce.add(t2) : M(t2);
        }
        delete this.childComponentLocations[e2];
      }
      disposeEventHandler(e2) {
        this.eventDelegator.removeListener(e2);
      }
      attachComponentToElement(e2, t2) {
        this.childComponentLocations[e2] = t2;
      }
      detachEventHandlersFromElement(e2) {
        for (const t2 of X(e2)) t2 instanceof Element && this.eventDelegator.removeListenersForElement(t2);
      }
      applyEdits(e2, t2, r2, o2, a2, s2) {
        let i2, c2 = 0, l2 = o2;
        const u2 = e2.arrayBuilderSegmentReader, d2 = e2.editReader, h2 = e2.frameReader, f2 = u2.values(a2), p2 = u2.offset(a2), m2 = p2 + u2.count(a2);
        for (let a3 = p2; a3 < m2; a3++) {
          const u3 = e2.diffReader.editsEntry(f2, a3), p3 = d2.editType(u3);
          switch (p3) {
            case n.prependFrame: {
              const n2 = d2.newTreeIndex(u3), o3 = e2.referenceFramesEntry(s2, n2), a4 = d2.siblingIndex(u3);
              this.insertFrame(e2, t2, r2, l2 + a4, s2, o3, n2);
              break;
            }
            case n.removeFrame:
              U(r2, l2 + d2.siblingIndex(u3));
              break;
            case n.setAttribute: {
              const n2 = d2.newTreeIndex(u3), o3 = e2.referenceFramesEntry(s2, n2), a4 = $(r2, l2 + d2.siblingIndex(u3));
              if (!(a4 instanceof Element)) throw new Error("Cannot set attribute on non-element child");
              this.applyAttribute(e2, t2, a4, o3);
              break;
            }
            case n.removeAttribute: {
              const e3 = $(r2, l2 + d2.siblingIndex(u3));
              if (!(e3 instanceof Element)) throw new Error("Cannot remove attribute from non-element child");
              {
                const t3 = d2.removedAttributeName(u3);
                this.setOrRemoveAttributeOrProperty(e3, t3, null);
              }
              break;
            }
            case n.updateText: {
              const t3 = d2.newTreeIndex(u3), n2 = e2.referenceFramesEntry(s2, t3), o3 = $(r2, l2 + d2.siblingIndex(u3));
              if (!(o3 instanceof Text)) throw new Error("Cannot set text content on non-text child");
              o3.textContent = h2.textContent(n2);
              break;
            }
            case n.updateMarkup: {
              const t3 = d2.newTreeIndex(u3), n2 = e2.referenceFramesEntry(s2, t3), o3 = d2.siblingIndex(u3);
              U(r2, l2 + o3), this.insertMarkup(e2, r2, l2 + o3, n2);
              break;
            }
            case n.stepIn:
              r2 = $(r2, l2 + d2.siblingIndex(u3)), c2++, l2 = 0;
              break;
            case n.stepOut:
              r2 = z(r2), c2--, l2 = 0 === c2 ? o2 : 0;
              break;
            case n.permutationListEntry:
              i2 = i2 || [], i2.push({ fromSiblingIndex: l2 + d2.siblingIndex(u3), toSiblingIndex: l2 + d2.moveToSiblingIndex(u3) });
              break;
            case n.permutationListEnd:
              Y(r2, i2), i2 = void 0;
              break;
            default:
              throw new Error(`Unknown edit type: ${p3}`);
          }
        }
      }
      insertFrame(e2, t2, n2, o2, a2, s2, i2) {
        const c2 = e2.frameReader, l2 = c2.frameType(s2);
        switch (l2) {
          case r.element:
            return this.insertElement(e2, t2, n2, o2, a2, s2, i2), 1;
          case r.text:
            return this.insertText(e2, n2, o2, s2), 1;
          case r.attribute:
            throw new Error("Attribute frames should only be present as leading children of element frames.");
          case r.component:
            return this.insertComponent(e2, n2, o2, s2), 1;
          case r.region:
            return this.insertFrameRange(e2, t2, n2, o2, a2, i2 + 1, i2 + c2.subtreeLength(s2));
          case r.elementReferenceCapture:
            if (n2 instanceof Element) return u2 = n2, d2 = c2.elementReferenceCaptureId(s2), u2.setAttribute(Q(d2), ""), 0;
            throw new Error("Reference capture frames can only be children of element frames.");
          case r.markup:
            return this.insertMarkup(e2, n2, o2, s2), 1;
          case r.namedEvent:
            return 0;
          default:
            throw new Error(`Unknown frame type: ${l2}`);
        }
        var u2, d2;
      }
      insertElement(e2, t2, n2, o2, a2, s2, i2) {
        const c2 = e2.frameReader, l2 = c2.elementName(s2), u2 = "svg" === l2 || W(n2) ? document.createElementNS("http://www.w3.org/2000/svg", l2) : document.createElement(l2), d2 = B(u2);
        let h2 = false;
        const f2 = i2 + c2.subtreeLength(s2);
        for (let s3 = i2 + 1; s3 < f2; s3++) {
          const i3 = e2.referenceFramesEntry(a2, s3);
          if (c2.frameType(i3) !== r.attribute) {
            J(u2, n2, o2), h2 = true, this.insertFrameRange(e2, t2, d2, 0, a2, s3, f2);
            break;
          }
          this.applyAttribute(e2, t2, u2, i3);
        }
        var p2;
        h2 || J(u2, n2, o2), (p2 = u2) instanceof HTMLOptionElement ? ae(p2) : te in p2 && oe(p2, p2[te]);
      }
      insertComponent(e2, t2, n2, r2) {
        const o2 = j(t2, n2), a2 = e2.frameReader.componentId(r2);
        this.attachComponentToElement(a2, o2);
      }
      insertText(e2, t2, n2, r2) {
        const o2 = e2.frameReader.textContent(r2);
        J(document.createTextNode(o2), t2, n2);
      }
      insertMarkup(e2, t2, n2, r2) {
        const o2 = j(t2, n2), a2 = (s2 = e2.frameReader.markupContent(r2), W(t2) ? (ie.innerHTML = s2 || " ", ie) : (se.innerHTML = s2 || " ", se.content.querySelectorAll("script").forEach((e3) => {
          const t3 = document.createElement("script");
          t3.textContent = e3.textContent, e3.getAttributeNames().forEach((n3) => {
            t3.setAttribute(n3, e3.getAttribute(n3));
          }), e3.parentNode.replaceChild(t3, e3);
        }), se.content));
        var s2;
        let i2 = 0;
        for (; a2.firstChild; ) J(a2.firstChild, o2, i2++);
      }
      applyAttribute(e2, t2, n2, r2) {
        const o2 = e2.frameReader, a2 = o2.attributeName(r2), s2 = o2.attributeEventHandlerId(r2);
        if (s2) {
          const e3 = pe(a2);
          return void this.eventDelegator.setListener(n2, e3, s2, t2);
        }
        const i2 = o2.attributeValue(r2);
        this.setOrRemoveAttributeOrProperty(n2, a2, i2);
      }
      insertFrameRange(e2, t2, n2, r2, o2, a2, s2) {
        const i2 = r2;
        for (let i3 = a2; i3 < s2; i3++) {
          const a3 = e2.referenceFramesEntry(o2, i3);
          r2 += this.insertFrame(e2, t2, n2, r2, o2, a3, i3), i3 += fe(e2, a3);
        }
        return r2 - i2;
      }
      setOrRemoveAttributeOrProperty(e2, t2, n2) {
        (function(e3, t3, n3) {
          switch (t3) {
            case "value":
              return function(e4, t4) {
                switch (t4 && "INPUT" === e4.tagName && (t4 = function(e5, t5) {
                  switch (t5.getAttribute("type")) {
                    case "time":
                      return 8 !== e5.length || !e5.endsWith("00") && t5.hasAttribute("step") ? e5 : e5.substring(0, 5);
                    case "datetime-local":
                      return 19 !== e5.length || !e5.endsWith("00") && t5.hasAttribute("step") ? e5 : e5.substring(0, 16);
                    default:
                      return e5;
                  }
                }(t4, e4)), e4.tagName) {
                  case "INPUT":
                  case "SELECT":
                  case "TEXTAREA":
                    return t4 && e4 instanceof HTMLSelectElement && ne(e4) && (t4 = JSON.parse(t4)), oe(e4, t4), e4[te] = t4, true;
                  case "OPTION":
                    return t4 || "" === t4 ? e4.setAttribute("value", t4) : e4.removeAttribute("value"), ae(e4), true;
                  default:
                    return false;
                }
              }(e3, n3);
            case "checked":
              return function(e4, t4) {
                return "INPUT" === e4.tagName && (e4.checked = null !== t4, true);
              }(e3, n3);
            default:
              return false;
          }
        })(e2, t2, n2) || (t2.startsWith("__internal_") ? this.applyInternalAttribute(e2, t2.substring(11), n2) : null !== n2 ? e2.setAttribute(t2, n2) : e2.removeAttribute(t2));
      }
      applyInternalAttribute(e2, t2, n2) {
        if (t2.startsWith("stopPropagation_")) {
          const r2 = pe(t2.substring(16));
          this.eventDelegator.setStopPropagation(e2, r2, null !== n2);
        } else {
          if (!t2.startsWith("preventDefault_")) throw new Error(`Unsupported internal attribute '${t2}'`);
          {
            const r2 = pe(t2.substring(15));
            this.eventDelegator.setPreventDefault(e2, r2, null !== n2);
          }
        }
      }
    }
    function he(e2, t2) {
      e2[le] = t2;
    }
    function fe(e2, t2) {
      const n2 = e2.frameReader;
      switch (n2.frameType(t2)) {
        case r.component:
        case r.element:
        case r.region:
          return n2.subtreeLength(t2) - 1;
        default:
          return 0;
      }
    }
    function pe(e2) {
      if (e2.startsWith("on")) return e2.substring(2);
      throw new Error(`Attribute should be an event name, but doesn't start with 'on'. Value: '${e2}'`);
    }
    const me = {};
    let be, ve, ge = false;
    function ye(e2) {
      const t2 = (n2 = document.baseURI).substring(0, n2.lastIndexOf("/"));
      var n2;
      const r2 = e2.charAt(t2.length);
      return e2.startsWith(t2) && ("" === r2 || "/" === r2 || "?" === r2 || "#" === r2);
    }
    function we(e2) {
      var _a;
      (_a = document.getElementById(e2)) == null ? void 0 : _a.scrollIntoView();
    }
    function Ee(e2) {
      return ve = ve || document.createElement("a"), ve.href = e2, ve.href;
    }
    function Se() {
      return void 0 !== be;
    }
    function Ce() {
      return be;
    }
    let Ie = false, De = 0, Ae = 0;
    const Ne = /* @__PURE__ */ new Map();
    let Re = async function(e2) {
      var _a, _b, _c;
      Fe();
      const t2 = Me();
      if (t2 == null ? void 0 : t2.hasLocationChangingEventListeners) {
        const n2 = (_b = (_a = e2.state) == null ? void 0 : _a._index) != null ? _b : 0, r2 = (_c = e2.state) == null ? void 0 : _c.userState, o2 = n2 - De, a2 = location.href;
        if (await xe(-o2), !await Pe(a2, r2, false, t2)) return;
        await xe(o2);
      }
      await He(true);
    }, Te = null;
    const ke = { listenForNavigationEvents: function(e2, t2, n2) {
      var _a, _b;
      Ne.set(e2, { rendererId: e2, hasLocationChangingEventListeners: false, locationChanged: t2, locationChanging: n2 }), Ie || (Ie = true, window.addEventListener("popstate", Be), De = (_b = (_a = history.state) == null ? void 0 : _a._index) != null ? _b : 0);
    }, enableNavigationInterception: function(e2) {
      if (void 0 !== be && be !== e2) throw new Error("Only one interactive runtime may enable navigation interception at a time.");
      be = e2;
    }, setHasLocationChangingListeners: function(e2, t2) {
      const n2 = Ne.get(e2);
      if (!n2) throw new Error(`Renderer with ID '${e2}' is not listening for navigation events`);
      n2.hasLocationChangingEventListeners = t2;
    }, endLocationChanging: function(e2, t2) {
      Te && e2 === Ae && (Te(t2), Te = null);
    }, navigateTo: function(e2, t2) {
      Oe(e2, t2, true);
    }, refresh: function(e2) {
      location.reload();
    }, getBaseURI: () => document.baseURI, getLocationHref: () => location.href, scrollToElement: we };
    function Oe(e2, t2, n2 = false) {
      const r2 = Ee(e2);
      !t2.forceLoad && ye(r2) ? _e(r2, false, t2.replaceHistoryEntry, t2.historyEntryState, n2) : function(e3, t3) {
        if (location.href === e3) {
          const t4 = e3 + "?";
          history.replaceState(null, "", t4), location.replace(e3);
        } else t3 ? location.replace(e3) : location.href = e3;
      }(e2, t2.replaceHistoryEntry);
    }
    async function _e(e2, t2, n2, r2 = void 0, o2 = false) {
      if (Fe(), function(e3, t3) {
        const n3 = new URL(e3), r3 = new URL(t3);
        return n3.origin === r3.origin && n3.pathname === r3.pathname && n3.search === r3.search && "" !== r3.hash;
      }(location.href, e2)) return Le(e2, n2, r2), void function(e3) {
        const t3 = e3.indexOf("#");
        t3 !== e3.length - 1 && we(e3.substring(t3 + 1));
      }(e2);
      const a2 = Me();
      (o2 || !(a2 == null ? void 0 : a2.hasLocationChangingEventListeners) || await Pe(e2, r2, t2, a2)) && (function(e3, t3) {
        const n3 = new URL(e3), r3 = new URL(t3);
        return n3.protocol === r3.protocol && n3.host === r3.host && n3.port === r3.port && n3.pathname === r3.pathname;
      }(e2, location.href) || (ge = true), Le(e2, n2, r2), await He(t2));
    }
    function Le(e2, t2, n2 = void 0) {
      t2 ? history.replaceState({ userState: n2, _index: De }, "", e2) : (De++, history.pushState({ userState: n2, _index: De }, "", e2));
    }
    function xe(e2) {
      return new Promise((t2) => {
        const n2 = Re;
        Re = () => {
          Re = n2, t2();
        }, history.go(e2);
      });
    }
    function Fe() {
      Te && (Te(false), Te = null);
    }
    function Pe(e2, t2, n2, r2) {
      return new Promise((o2) => {
        Fe(), Ae++, Te = o2, r2.locationChanging(Ae, e2, t2, n2);
      });
    }
    async function He(e2, t2) {
      const n2 = location.href;
      await Promise.all(Array.from(Ne, async ([t3, r2]) => {
        var _a;
        D(t3) && await r2.locationChanged(n2, (_a = history.state) == null ? void 0 : _a.userState, e2);
      }));
    }
    async function Be(e2) {
      var _a, _b;
      Re && (Se(), 1) && await Re(e2), De = (_b = (_a = history.state) == null ? void 0 : _a._index) != null ? _b : 0;
    }
    function Me() {
      const e2 = Ce();
      if (void 0 !== e2) return Ne.get(e2);
    }
    const je = { focus: function(e2, t2) {
      if (e2 instanceof HTMLElement) e2.focus({ preventScroll: t2 });
      else {
        if (!(e2 instanceof SVGElement)) throw new Error("Unable to focus an invalid element.");
        if (!e2.hasAttribute("tabindex")) throw new Error("Unable to focus an SVG element that does not have a tabindex.");
        e2.focus({ preventScroll: t2 });
      }
    }, focusBySelector: function(e2) {
      const t2 = document.querySelector(e2);
      t2 && (t2.hasAttribute("tabindex") || (t2.tabIndex = -1), t2.focus({ preventScroll: true }));
    } }, Je = { init: function(e2, t2, n2, r2 = 50) {
      const o2 = ze(t2);
      (o2 || document.documentElement).style.overflowAnchor = "none";
      const a2 = document.createRange();
      h2(n2.parentElement) && (t2.style.display = "table-row", n2.style.display = "table-row");
      const s2 = new IntersectionObserver(function(r3) {
        r3.forEach((r4) => {
          var _a;
          if (!r4.isIntersecting) return;
          a2.setStartAfter(t2), a2.setEndBefore(n2);
          const o3 = a2.getBoundingClientRect().height, s3 = (_a = r4.rootBounds) == null ? void 0 : _a.height;
          r4.target === t2 ? e2.invokeMethodAsync("OnSpacerBeforeVisible", r4.intersectionRect.top - r4.boundingClientRect.top, o3, s3) : r4.target === n2 && n2.offsetHeight > 0 && e2.invokeMethodAsync("OnSpacerAfterVisible", r4.boundingClientRect.bottom - r4.intersectionRect.bottom, o3, s3);
        });
      }, { root: o2, rootMargin: `${r2}px` });
      s2.observe(t2), s2.observe(n2);
      const i2 = d2(t2), c2 = d2(n2), { observersByDotNetObjectId: l2, id: u2 } = $e(e2);
      function d2(e3) {
        const t3 = { attributes: true }, n3 = new MutationObserver((n4, r3) => {
          h2(e3.parentElement) && (r3.disconnect(), e3.style.display = "table-row", r3.observe(e3, t3)), s2.unobserve(e3), s2.observe(e3);
        });
        return n3.observe(e3, t3), n3;
      }
      function h2(e3) {
        return null !== e3 && (e3 instanceof HTMLTableElement && "" === e3.style.display || "table" === e3.style.display || e3 instanceof HTMLTableSectionElement && "" === e3.style.display || "table-row-group" === e3.style.display);
      }
      l2[u2] = { intersectionObserver: s2, mutationObserverBefore: i2, mutationObserverAfter: c2 };
    }, dispose: function(e2) {
      const { observersByDotNetObjectId: t2, id: n2 } = $e(e2), r2 = t2[n2];
      r2 && (r2.intersectionObserver.disconnect(), r2.mutationObserverBefore.disconnect(), r2.mutationObserverAfter.disconnect(), e2.dispose(), delete t2[n2]);
    } }, Ue = Symbol();
    function ze(e2) {
      return e2 && e2 !== document.body && e2 !== document.documentElement ? "visible" !== getComputedStyle(e2).overflowY ? e2 : ze(e2.parentElement) : null;
    }
    function $e(e2) {
      var _a;
      const t2 = e2._callDispatcher, n2 = e2._id;
      return (_a = t2[Ue]) != null ? _a : t2[Ue] = {}, { observersByDotNetObjectId: t2[Ue], id: n2 };
    }
    const We = { getAndRemoveExistingTitle: function() {
      var _a;
      const e2 = document.head ? document.head.getElementsByTagName("title") : [];
      if (0 === e2.length) return null;
      let t2 = null;
      for (let n2 = e2.length - 1; n2 >= 0; n2--) {
        const r2 = e2[n2], o2 = r2.previousSibling;
        o2 instanceof Comment && null !== z(o2) || (null === t2 && (t2 = r2.textContent), (_a = r2.parentNode) == null ? void 0 : _a.removeChild(r2));
      }
      return t2;
    } }, Ke = { init: function(e2, t2) {
      t2._blazorInputFileNextFileId = 0, t2.addEventListener("click", function() {
        t2.value = "";
      }), t2.addEventListener("change", function() {
        t2._blazorFilesById = {};
        const n2 = Array.prototype.map.call(t2.files, function(e3) {
          const n3 = { id: ++t2._blazorInputFileNextFileId, lastModified: new Date(e3.lastModified).toISOString(), name: e3.name, size: e3.size, contentType: e3.type, readPromise: void 0, arrayBuffer: void 0, blob: e3 };
          return t2._blazorFilesById[n3.id] = n3, n3;
        });
        e2.invokeMethodAsync("NotifyChange", n2);
      });
    }, toImageFile: async function(e2, t2, n2, r2, o2) {
      const a2 = Ve(e2, t2), s2 = await new Promise(function(e3) {
        const t3 = new Image();
        t3.onload = function() {
          URL.revokeObjectURL(t3.src), e3(t3);
        }, t3.onerror = function() {
          t3.onerror = null, URL.revokeObjectURL(t3.src);
        }, t3.src = URL.createObjectURL(a2.blob);
      }), i2 = await new Promise(function(e3) {
        var _a;
        const t3 = Math.min(1, r2 / s2.width), a3 = Math.min(1, o2 / s2.height), i3 = Math.min(t3, a3), c3 = document.createElement("canvas");
        c3.width = Math.round(s2.width * i3), c3.height = Math.round(s2.height * i3), (_a = c3.getContext("2d")) == null ? void 0 : _a.drawImage(s2, 0, 0, c3.width, c3.height), c3.toBlob(e3, n2);
      }), c2 = { id: ++e2._blazorInputFileNextFileId, lastModified: a2.lastModified, name: a2.name, size: (i2 == null ? void 0 : i2.size) || 0, contentType: n2, blob: i2 || a2.blob };
      return e2._blazorFilesById[c2.id] = c2, c2;
    }, readFileData: async function(e2, t2) {
      return Ve(e2, t2).blob;
    } };
    function Ve(e2, t2) {
      const n2 = e2._blazorFilesById[t2];
      if (!n2) throw new Error(`There is no file with ID ${t2}. The file list may have changed. See https://aka.ms/aspnet/blazor-input-file-multiple-selections.`);
      return n2;
    }
    const Xe = /* @__PURE__ */ new Set(), Ye = { enableNavigationPrompt: function(e2) {
      0 === Xe.size && window.addEventListener("beforeunload", Ge), Xe.add(e2);
    }, disableNavigationPrompt: function(e2) {
      Xe.delete(e2), 0 === Xe.size && window.removeEventListener("beforeunload", Ge);
    } };
    function Ge(e2) {
      e2.preventDefault(), e2.returnValue = true;
    }
    const qe = /* @__PURE__ */ new Map(), Ze = { navigateTo: function(e2, t2, n2 = false) {
      Oe(e2, t2 instanceof Object ? t2 : { forceLoad: t2, replaceHistoryEntry: n2 });
    }, registerCustomEventType: function(e2, t2) {
      if (!t2) throw new Error("The options parameter is required.");
      if (a.has(e2)) throw new Error(`The event '${e2}' is already registered.`);
      if (t2.browserEventName) {
        const n2 = s.get(t2.browserEventName);
        n2 ? n2.push(e2) : s.set(t2.browserEventName, [e2]), i.forEach((n3) => n3(e2, t2.browserEventName));
      }
      a.set(e2, t2);
    }, rootComponents: g, runtime: {}, _internal: { navigationManager: ke, domWrapper: je, Virtualize: Je, PageTitle: We, InputFile: Ke, NavigationLock: Ye, getJSDataStreamChunk: async function(e2, t2, n2) {
      return e2 instanceof Blob ? await async function(e3, t3, n3) {
        const r2 = e3.slice(t3, t3 + n3), o2 = await r2.arrayBuffer();
        return new Uint8Array(o2);
      }(e2, t2, n2) : function(e3, t3, n3) {
        return new Uint8Array(e3.buffer, e3.byteOffset + t3, n3);
      }(e2, t2, n2);
    }, attachWebRendererInterop: function(e2, n2, r2, o2) {
      var _a, _b;
      if (S.has(e2)) throw new Error(`Interop methods are already registered for renderer ${e2}`);
      S.set(e2, n2), r2 && o2 && Object.keys(r2).length > 0 && function(e3, n3, r3) {
        if (m) throw new Error("Dynamic root components have already been enabled.");
        m = e3, b = n3;
        for (const [e4, o3] of Object.entries(r3)) {
          const r4 = t.findJSFunction(e4, 0);
          for (const e5 of o3) r4(e5, n3[e5]);
        }
      }(N(e2), r2, o2), (_b = (_a = I.get(e2)) == null ? void 0 : _a[0]) == null ? void 0 : _b.call(_a), function(e3) {
        for (const t2 of C) t2(e3);
      }(e2);
    } } };
    window.Blazor = Ze;
    let Qe = false;
    const et = "function" == typeof TextDecoder ? new TextDecoder("utf-8") : null, tt = et ? et.decode.bind(et) : function(e2) {
      let t2 = 0;
      const n2 = e2.length, r2 = [], o2 = [];
      for (; t2 < n2; ) {
        const n3 = e2[t2++];
        if (0 === n3) break;
        if (128 & n3) {
          if (192 == (224 & n3)) {
            const o3 = 63 & e2[t2++];
            r2.push((31 & n3) << 6 | o3);
          } else if (224 == (240 & n3)) {
            const o3 = 63 & e2[t2++], a2 = 63 & e2[t2++];
            r2.push((31 & n3) << 12 | o3 << 6 | a2);
          } else if (240 == (248 & n3)) {
            let o3 = (7 & n3) << 18 | (63 & e2[t2++]) << 12 | (63 & e2[t2++]) << 6 | 63 & e2[t2++];
            o3 > 65535 && (o3 -= 65536, r2.push(o3 >>> 10 & 1023 | 55296), o3 = 56320 | 1023 & o3), r2.push(o3);
          }
        } else r2.push(n3);
        r2.length > 1024 && (o2.push(String.fromCharCode.apply(null, r2)), r2.length = 0);
      }
      return o2.push(String.fromCharCode.apply(null, r2)), o2.join("");
    }, nt = Math.pow(2, 32), rt = Math.pow(2, 21) - 1;
    function ot(e2, t2) {
      return e2[t2] | e2[t2 + 1] << 8 | e2[t2 + 2] << 16 | e2[t2 + 3] << 24;
    }
    function at(e2, t2) {
      return e2[t2] + (e2[t2 + 1] << 8) + (e2[t2 + 2] << 16) + (e2[t2 + 3] << 24 >>> 0);
    }
    function st(e2, t2) {
      const n2 = at(e2, t2 + 4);
      if (n2 > rt) throw new Error(`Cannot read uint64 with high order part ${n2}, because the result would exceed Number.MAX_SAFE_INTEGER.`);
      return n2 * nt + at(e2, t2);
    }
    class it {
      constructor(e2) {
        this.batchData = e2;
        const t2 = new dt(e2);
        this.arrayRangeReader = new ht(e2), this.arrayBuilderSegmentReader = new ft(e2), this.diffReader = new ct(e2), this.editReader = new lt(e2, t2), this.frameReader = new ut(e2, t2);
      }
      updatedComponents() {
        return ot(this.batchData, this.batchData.length - 20);
      }
      referenceFrames() {
        return ot(this.batchData, this.batchData.length - 16);
      }
      disposedComponentIds() {
        return ot(this.batchData, this.batchData.length - 12);
      }
      disposedEventHandlerIds() {
        return ot(this.batchData, this.batchData.length - 8);
      }
      updatedComponentsEntry(e2, t2) {
        const n2 = e2 + 4 * t2;
        return ot(this.batchData, n2);
      }
      referenceFramesEntry(e2, t2) {
        return e2 + 20 * t2;
      }
      disposedComponentIdsEntry(e2, t2) {
        const n2 = e2 + 4 * t2;
        return ot(this.batchData, n2);
      }
      disposedEventHandlerIdsEntry(e2, t2) {
        const n2 = e2 + 8 * t2;
        return st(this.batchData, n2);
      }
    }
    class ct {
      constructor(e2) {
        this.batchDataUint8 = e2;
      }
      componentId(e2) {
        return ot(this.batchDataUint8, e2);
      }
      edits(e2) {
        return e2 + 4;
      }
      editsEntry(e2, t2) {
        return e2 + 16 * t2;
      }
    }
    class lt {
      constructor(e2, t2) {
        this.batchDataUint8 = e2, this.stringReader = t2;
      }
      editType(e2) {
        return ot(this.batchDataUint8, e2);
      }
      siblingIndex(e2) {
        return ot(this.batchDataUint8, e2 + 4);
      }
      newTreeIndex(e2) {
        return ot(this.batchDataUint8, e2 + 8);
      }
      moveToSiblingIndex(e2) {
        return ot(this.batchDataUint8, e2 + 8);
      }
      removedAttributeName(e2) {
        const t2 = ot(this.batchDataUint8, e2 + 12);
        return this.stringReader.readString(t2);
      }
    }
    class ut {
      constructor(e2, t2) {
        this.batchDataUint8 = e2, this.stringReader = t2;
      }
      frameType(e2) {
        return ot(this.batchDataUint8, e2);
      }
      subtreeLength(e2) {
        return ot(this.batchDataUint8, e2 + 4);
      }
      elementReferenceCaptureId(e2) {
        const t2 = ot(this.batchDataUint8, e2 + 4);
        return this.stringReader.readString(t2);
      }
      componentId(e2) {
        return ot(this.batchDataUint8, e2 + 8);
      }
      elementName(e2) {
        const t2 = ot(this.batchDataUint8, e2 + 8);
        return this.stringReader.readString(t2);
      }
      textContent(e2) {
        const t2 = ot(this.batchDataUint8, e2 + 4);
        return this.stringReader.readString(t2);
      }
      markupContent(e2) {
        const t2 = ot(this.batchDataUint8, e2 + 4);
        return this.stringReader.readString(t2);
      }
      attributeName(e2) {
        const t2 = ot(this.batchDataUint8, e2 + 4);
        return this.stringReader.readString(t2);
      }
      attributeValue(e2) {
        const t2 = ot(this.batchDataUint8, e2 + 8);
        return this.stringReader.readString(t2);
      }
      attributeEventHandlerId(e2) {
        return st(this.batchDataUint8, e2 + 12);
      }
    }
    class dt {
      constructor(e2) {
        this.batchDataUint8 = e2, this.stringTableStartIndex = ot(e2, e2.length - 4);
      }
      readString(e2) {
        if (-1 === e2) return null;
        {
          const n2 = ot(this.batchDataUint8, this.stringTableStartIndex + 4 * e2), r2 = function(e3, t3) {
            let n3 = 0, r3 = 0;
            for (let o3 = 0; o3 < 4; o3++) {
              const a3 = e3[t3 + o3];
              if (n3 |= (127 & a3) << r3, a3 < 128) break;
              r3 += 7;
            }
            return n3;
          }(this.batchDataUint8, n2), o2 = n2 + ((t2 = r2) < 128 ? 1 : t2 < 16384 ? 2 : t2 < 2097152 ? 3 : 4), a2 = new Uint8Array(this.batchDataUint8.buffer, this.batchDataUint8.byteOffset + o2, r2);
          return tt(a2);
        }
        var t2;
      }
    }
    class ht {
      constructor(e2) {
        this.batchDataUint8 = e2;
      }
      count(e2) {
        return ot(this.batchDataUint8, e2);
      }
      values(e2) {
        return e2 + 4;
      }
    }
    class ft {
      constructor(e2) {
        this.batchDataUint8 = e2;
      }
      offset(e2) {
        return 0;
      }
      count(e2) {
        return ot(this.batchDataUint8, e2);
      }
      values(e2) {
        return e2 + 4;
      }
    }
    const pt = "__bwv:";
    let mt = false;
    function bt(e2, t2) {
      St("OnRenderCompleted", e2, t2);
    }
    function vt(e2, t2, n2, r2, o2) {
      St("BeginInvokeDotNet", e2 ? e2.toString() : null, t2, n2, r2 || 0, o2);
    }
    function gt(e2, t2, n2) {
      St("EndInvokeJS", e2, t2, n2);
    }
    function yt(e2, t2) {
      const n2 = function(e3) {
        const t3 = new Array(e3.length);
        for (let n3 = 0; n3 < e3.length; n3++) t3[n3] = String.fromCharCode(e3[n3]);
        return btoa(t3.join(""));
      }(t2);
      St("ReceiveByteArrayFromJS", e2, n2);
    }
    function wt(e2, t2, n2) {
      return St("OnLocationChanged", e2, t2, n2), Promise.resolve();
    }
    function Et(e2, t2, n2, r2) {
      return St("OnLocationChanging", e2, t2, n2, r2), Promise.resolve();
    }
    function St(e2, ...t2) {
      const n2 = function(e3, t3) {
        return mt ? null : `${pt}${JSON.stringify([e3, ...t3])}`;
      }(e2, t2);
      n2 && window.external.sendMessage(n2);
    }
    var Ct, It;
    function Dt(t2, n2) {
      const r2 = At(n2);
      e.dispatcher.receiveByteArray(t2, r2);
    }
    function At(e2) {
      const t2 = atob(e2), n2 = t2.length, r2 = new Uint8Array(n2);
      for (let e3 = 0; e3 < n2; e3++) r2[e3] = t2.charCodeAt(e3);
      return r2;
    }
    !function(e2) {
      e2[e2.Default = 0] = "Default", e2[e2.Server = 1] = "Server", e2[e2.WebAssembly = 2] = "WebAssembly", e2[e2.WebView = 3] = "WebView";
    }(Ct || (Ct = {})), function(e2) {
      e2[e2.Trace = 0] = "Trace", e2[e2.Debug = 1] = "Debug", e2[e2.Information = 2] = "Information", e2[e2.Warning = 3] = "Warning", e2[e2.Error = 4] = "Error", e2[e2.Critical = 5] = "Critical", e2[e2.None = 6] = "None";
    }(It || (It = {}));
    class Nt {
      constructor(e2 = true, t2, n2, r2 = 0) {
        this.singleRuntime = e2, this.logger = t2, this.webRendererId = r2, this.afterStartedCallbacks = [], n2 && this.afterStartedCallbacks.push(...n2);
      }
      async importInitializersAsync(e2, t2) {
        await Promise.all(e2.map((e3) => async function(e4, n2) {
          let r2;
          var o2;
          n2.moduleExports || (o2 = n2.name, r2 = new URL(o2, document.baseURI).toString(), n2.moduleExports = await import(r2));
          const a2 = n2.moduleExports;
          if (void 0 !== a2) {
            if (e4.singleRuntime) {
              const { beforeStart: n3, afterStarted: r3, beforeWebAssemblyStart: o3, afterWebAssemblyStarted: i2, beforeServerStart: c2, afterServerStarted: l2 } = a2;
              let u2 = n3;
              e4.webRendererId === Ct.Server && c2 && (u2 = c2), e4.webRendererId === Ct.WebAssembly && o3 && (u2 = o3);
              let d2 = r3;
              return e4.webRendererId === Ct.Server && l2 && (d2 = l2), e4.webRendererId === Ct.WebAssembly && i2 && (d2 = i2), s2(e4, u2, d2, t2);
            }
            return function(e5, t3, n3) {
              var _a;
              const o3 = n3[0], { beforeStart: a3, afterStarted: i2, beforeWebStart: c2, afterWebStarted: l2, beforeWebAssemblyStart: u2, afterWebAssemblyStarted: d2, beforeServerStart: h2, afterServerStarted: f2 } = t3, p2 = !(c2 || l2 || u2 || d2 || h2 || f2 || !a3 && !i2), m2 = p2 && o3.enableClassicInitializers;
              if (p2 && !o3.enableClassicInitializers) (_a = e5.logger) == null ? void 0 : _a.log(It.Warning, `Initializer '${r2}' will be ignored because multiple runtimes are available. Use 'before(Web|WebAssembly|Server)Start' and 'after(Web|WebAssembly|Server)Started' instead.`);
              else if (m2) return s2(e5, a3, i2, n3);
              if (function(e6) {
                e6.webAssembly ? e6.webAssembly.initializers || (e6.webAssembly.initializers = { beforeStart: [], afterStarted: [] }) : e6.webAssembly = { initializers: { beforeStart: [], afterStarted: [] } }, e6.circuit ? e6.circuit.initializers || (e6.circuit.initializers = { beforeStart: [], afterStarted: [] }) : e6.circuit = { initializers: { beforeStart: [], afterStarted: [] } };
              }(o3), u2 && o3.webAssembly.initializers.beforeStart.push(u2), d2 && o3.webAssembly.initializers.afterStarted.push(d2), h2 && o3.circuit.initializers.beforeStart.push(h2), f2 && o3.circuit.initializers.afterStarted.push(f2), l2 && e5.afterStartedCallbacks.push(l2), c2) return c2(o3);
            }(e4, a2, t2);
          }
          function s2(e5, t3, n3, r3) {
            if (n3 && e5.afterStartedCallbacks.push(n3), t3) return t3(...r3);
          }
        }(this, e3)));
      }
      async invokeAfterStartedCallbacks(e2) {
        var _a;
        const t2 = (n2 = this.webRendererId, (_a = I.get(n2)) == null ? void 0 : _a[1]);
        var n2;
        t2 && await t2, await Promise.all(this.afterStartedCallbacks.map((t3) => t3(e2)));
      }
    }
    let Rt = false;
    async function Tt() {
      if (Rt) throw new Error("Blazor has already started.");
      Rt = true, e.dispatcher = t.attachDispatcher({ beginInvokeDotNetFromJS: vt, endInvokeJSFromDotNet: gt, sendByteArray: yt });
      const n2 = await async function() {
        const e2 = await fetch("_framework/blazor.modules.json", { method: "GET", credentials: "include", cache: "no-cache" }), t2 = (await e2.json()).map((e3) => ({ name: e3 })), n3 = new Nt();
        return await n3.importInitializersAsync(t2, []), n3;
      }();
      (function() {
        const t2 = { AttachToDocument: (e2, t3) => {
          !function(e3, t4, n3) {
            const r2 = "::before";
            let o2 = false;
            if (e3.endsWith("::after")) e3 = e3.slice(0, -7), o2 = true;
            else if (e3.endsWith(r2)) throw new Error(`The '${r2}' selector is not supported.`);
            const a2 = function(e4) {
              const t5 = p.get(e4);
              if (t5) return p.delete(e4), t5;
            }(e3) || document.querySelector(e3);
            if (!a2) throw new Error(`Could not find any element matching selector '${e3}'.`);
            !function(e4, t5, n4, r3) {
              let o3 = me[e4];
              o3 || (o3 = new de(e4), me[e4] = o3), o3.attachRootComponentToLogicalElement(n4, t5, r3);
            }(n3, B(a2, true), t4, o2);
          }(t3, e2, Ct.WebView);
        }, RenderBatch: (e2, t3) => {
          try {
            const n3 = At(t3);
            (function(e3, t4) {
              const n4 = me[e3];
              if (!n4) throw new Error(`There is no browser renderer with ID ${e3}.`);
              const r2 = t4.arrayRangeReader, o2 = t4.updatedComponents(), a2 = r2.values(o2), s2 = r2.count(o2), i2 = t4.referenceFrames(), c2 = r2.values(i2), l2 = t4.diffReader;
              for (let e4 = 0; e4 < s2; e4++) {
                const r3 = t4.updatedComponentsEntry(a2, e4), o3 = l2.componentId(r3), s3 = l2.edits(r3);
                n4.updateComponent(t4, o3, s3, c2);
              }
              const u2 = t4.disposedComponentIds(), d2 = r2.values(u2), h2 = r2.count(u2);
              for (let e4 = 0; e4 < h2; e4++) {
                const r3 = t4.disposedComponentIdsEntry(d2, e4);
                n4.disposeComponent(r3);
              }
              const f2 = t4.disposedEventHandlerIds(), p2 = r2.values(f2), m2 = r2.count(f2);
              for (let e4 = 0; e4 < m2; e4++) {
                const r3 = t4.disposedEventHandlerIdsEntry(p2, e4);
                n4.disposeEventHandler(r3);
              }
              ge && (ge = false, window.scrollTo && window.scrollTo(0, 0));
            })(Ct.WebView, new it(n3)), bt(e2, null);
          } catch (t4) {
            bt(e2, t4.toString());
          }
        }, NotifyUnhandledException: (e2, t3) => {
          mt = true, console.error(`${e2}
${t3}`), function() {
            const e3 = document.querySelector("#blazor-error-ui");
            e3 && (e3.style.display = "block"), Qe || (Qe = true, document.querySelectorAll("#blazor-error-ui .reload").forEach((e4) => {
              e4.onclick = function(e5) {
                location.reload(), e5.preventDefault();
              };
            }), document.querySelectorAll("#blazor-error-ui .dismiss").forEach((e4) => {
              e4.onclick = function(e5) {
                const t4 = document.querySelector("#blazor-error-ui");
                t4 && (t4.style.display = "none"), e5.preventDefault();
              };
            }));
          }();
        }, BeginInvokeJS: e.dispatcher.beginInvokeJSFromDotNet.bind(e.dispatcher), EndInvokeDotNet: e.dispatcher.endInvokeDotNetFromJS.bind(e.dispatcher), SendByteArrayToJS: Dt, Navigate: ke.navigateTo, Refresh: ke.refresh, SetHasLocationChangingListeners: (e2) => {
          ke.setHasLocationChangingListeners(Ct.WebView, e2);
        }, EndLocationChanging: ke.endLocationChanging };
        window.external.receiveMessage((e2) => {
          const n3 = function(e3) {
            if (mt || !e3 || !e3.startsWith(pt)) return null;
            const t3 = e3.substring(6), [n4, ...r2] = JSON.parse(t3);
            return { messageType: n4, args: r2 };
          }(e2);
          if (n3) {
            if (!Object.prototype.hasOwnProperty.call(t2, n3.messageType)) throw new Error(`Unsupported IPC message type '${n3.messageType}'`);
            t2[n3.messageType].apply(null, n3.args);
          }
        });
      })(), Ze._internal.receiveWebViewDotNetDataStream = kt, ke.enableNavigationInterception(Ct.WebView), ke.listenForNavigationEvents(Ct.WebView, wt, Et), St("AttachPage", ke.getBaseURI(), ke.getLocationHref()), await n2.invokeAfterStartedCallbacks(Ze);
    }
    function kt(t2, n2, r2, o2) {
      !function(e2, t3, n3, r3, o3) {
        let a2 = qe.get(t3);
        if (!a2) {
          const n4 = new ReadableStream({ start(e3) {
            qe.set(t3, e3), a2 = e3;
          } });
          e2.supplyDotNetStream(t3, n4);
        }
        o3 ? (a2.error(o3), qe.delete(t3)) : 0 === r3 ? (a2.close(), qe.delete(t3)) : a2.enqueue(n3.length === r3 ? n3 : n3.subarray(0, r3));
      }(e.dispatcher, t2, n2, r2, o2);
    }
    e.dispatcher = void 0, Ze.start = Tt, window.DotNet = t, document && document.currentScript && "false" !== document.currentScript.getAttribute("autostart") && Tt();
  }({});
})();
