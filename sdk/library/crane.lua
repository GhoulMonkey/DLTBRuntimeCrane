-- SPDX-License-Identifier: MIT
---@meta CRANE

---@alias CraneValue boolean|integer|number|string
---@alias CraneStatus string
---@alias CraneSubject integer
---@alias CraneLease integer
---@alias CraneModifier integer
---@alias CranePhase 1|2

---@class CranePathInfo
---@field type integer Bridge value-type enum
---@field access integer Bridge access-bit mask
---@field scopes integer Bridge scope-bit mask
---@field tier integer Bridge stability-tier enum

---@class CraneEvent
---@field name string
---@field scope integer
---@field suppressed boolean
---@field data table<string, CraneValue>

---@class CraneBridge
local CraneBridge = {}

---@return integer scope
function CraneBridge.scope() end

---@param text string
function CraneBridge.log(text) end

---@param path string
---@param subject? CraneSubject
---@return CraneValue? value
---@return CraneStatus? error
function CraneBridge.get(path, subject) end

---@param path string
---@return CranePathInfo? info
---@return CraneStatus? error
function CraneBridge.describe(path) end

---@param prefix? string
---@return string[]? names
---@return integer|string total_or_error
function CraneBridge.list(prefix) end

---@param path string Subject path such as `player`, `hunger`, or `player.flashlight`.
---@return CraneSubject? subject
---@return CraneStatus? error
function CraneBridge.resolve(path) end

---@param name string
---@param phase CranePhase 1=BEFORE, 2=AFTER
---@param callback fun(event: CraneEvent): boolean?
---@return integer? subscription
---@return CraneStatus? error
function CraneBridge.on(name, phase, callback) end

---@param path string
---@param value CraneValue
---@param subject? CraneSubject
---@return true? ok
---@return CraneStatus? error
function CraneBridge.set(path, value, subject) end

---@param path string
---@param subject? CraneSubject
---@return CraneLease? handle
---@return CraneStatus? error
function CraneBridge.claim(path, subject) end

---@param handle CraneLease
---@param value CraneValue
---@return true? ok
---@return CraneStatus? error
function CraneBridge.lease_write(handle, value) end

---@param handle CraneLease
---@return CraneValue? baseline
---@return CraneStatus? error
function CraneBridge.lease_baseline(handle) end

---@param handle CraneLease
---@return true? ok
---@return CraneStatus? error
function CraneBridge.lease_release(handle) end

---@param path string
---@param subject? CraneSubject
---@return CraneModifier? handle
---@return CraneStatus? error
function CraneBridge.modifier_acquire(path, subject) end

---@param handle CraneModifier
---@param value CraneValue
---@return true? ok
---@return CraneStatus? error
function CraneBridge.modifier_write(handle, value) end

---@param handle CraneModifier
---@return CraneValue? contribution
---@return CraneValue|CraneStatus? effective_or_error
function CraneBridge.modifier_read(handle) end

---@param handle CraneModifier
---@return true? ok
---@return CraneStatus? error
function CraneBridge.modifier_release(handle) end

---@type CraneBridge
bridge = bridge

---@type table<string, CraneValue>
params = params

