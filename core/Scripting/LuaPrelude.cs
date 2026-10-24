namespace Core.Scripting
{
    /// <summary>
    /// TTS's Lua classes written in Lua, loaded into every script before its own code: <c>Vector</c> and <c>Color</c>
    /// (constructors, operators, the methods scripts use). Vectors and colours the API returns carry the same metatables.
    /// </summary>
    internal static class LuaPrelude
    {
        public const string VectorMeta = "__tw_vector_meta";
        public const string ColorMeta = "__tw_color_meta";

        public const string Source = @"
local sqrt, acos, deg, rad, cos, sin, abs = math.sqrt, math.acos, math.deg, math.rad, math.cos, math.sin, math.abs
local axes = { 'x', 'y', 'z' }

local V = {}
local VM = {}
__tw_vector_meta = VM
VM.__index = function(v, k)
  if type(k) == 'number' then return rawget(v, axes[k]) end
  return V[k]
end
VM.__newindex = function(v, k, value)
  if type(k) == 'number' and axes[k] then rawset(v, axes[k], value) else rawset(v, k, value) end
end
local function vec(x, y, z) return setmetatable({ x = x or 0, y = y or 0, z = z or 0 }, VM) end
local function parts(a)
  if type(a) == 'number' then return a, a, a end
  return rawget(a, 'x') or a[1] or 0, rawget(a, 'y') or a[2] or 0, rawget(a, 'z') or a[3] or 0
end
Vector = setmetatable({}, { __index = V, __call = function(_, x, y, z)
  if type(x) == 'table' then return vec(parts(x)) end
  return vec(x, y, z)
end })
V.new = function(...) return Vector(...) end
VM.__add = function(a, b) local ax, ay, az = parts(a); local bx, by, bz = parts(b); return vec(ax + bx, ay + by, az + bz) end
VM.__sub = function(a, b) local ax, ay, az = parts(a); local bx, by, bz = parts(b); return vec(ax - bx, ay - by, az - bz) end
VM.__mul = function(a, b) local ax, ay, az = parts(a); local bx, by, bz = parts(b); return vec(ax * bx, ay * by, az * bz) end
VM.__div = function(a, b) local ax, ay, az = parts(a); local bx, by, bz = parts(b); return vec(ax / bx, ay / by, az / bz) end
VM.__unm = function(a) return vec(-a.x, -a.y, -a.z) end
VM.__eq = function(a, b) return V.equals(a, b) end
VM.__tostring = function(v) return string.format('{%g, %g, %g}', v.x, v.y, v.z) end
function V.copy(v) return vec(v.x, v.y, v.z) end
function V.get(v) return v.x, v.y, v.z end
function V.set(v, x, y, z) v.x = x or v.x; v.y = y or v.y; v.z = z or v.z; return v end
function V.setAt(v, k, value) v[k] = value; return v end
function V.add(v, o) local x, y, z = parts(o); v.x = v.x + x; v.y = v.y + y; v.z = v.z + z; return v end
function V.sub(v, o) local x, y, z = parts(o); v.x = v.x - x; v.y = v.y - y; v.z = v.z - z; return v end
function V.scale(v, o) local x, y, z = parts(o); v.x = v.x * x; v.y = v.y * y; v.z = v.z * z; return v end
function V.clamp(v, m) local l = V.magnitude(v); if l > m and l > 0 then V.scale(v, m / l) end; return v end
function V.sqrMagnitude(v) return v.x * v.x + v.y * v.y + v.z * v.z end
function V.magnitude(v) return sqrt(V.sqrMagnitude(v)) end
function V.normalize(v) local l = V.magnitude(v); if l > 0 then V.scale(v, 1 / l) end; return v end
function V.normalized(v) return V.normalize(V.copy(v)) end
function V.sqrDistance(a, b) local x, y, z = parts(b); return (a.x - x)^2 + (a.y - y)^2 + (a.z - z)^2 end
function V.distance(a, b) return sqrt(V.sqrDistance(a, b)) end
function V.dot(a, b) local x, y, z = parts(b); return a.x * x + a.y * y + a.z * z end
function V.cross(a, b) local x, y, z = parts(b); return vec(a.y * z - a.z * y, a.z * x - a.x * z, a.x * y - a.y * x) end
function V.angle(a, b)
  local m = V.magnitude(a) * Vector(b):magnitude()
  if m == 0 then return 0 end
  return deg(acos(math.max(-1, math.min(1, V.dot(a, b) / m))))
end
function V.lerp(a, b, t) local x, y, z = parts(b); return vec(a.x + (x - a.x) * t, a.y + (y - a.y) * t, a.z + (z - a.z) * t) end
function V.inverse(v) v.x = -v.x; v.y = -v.y; v.z = -v.z; return v end
function V.equals(a, b, margin)
  local x, y, z = parts(b); margin = margin or 1e-3
  return abs(a.x - x) <= margin and abs(a.y - y) <= margin and abs(a.z - z) <= margin
end
function V.string(v, prefix) return (prefix or '') .. tostring(v) end
function V.heading(v, axis)
  if axis == 'x' then return deg(math.atan2(v.y, v.z)) elseif axis == 'z' then return deg(math.atan2(v.x, v.y)) end
  return deg(math.atan2(v.x, v.z))
end
function V.rotateOver(v, axis, angle)
  local a = rad(angle); local c, s = cos(a), sin(a)
  if axis == 'x' then v.y, v.z = v.y * c - v.z * s, v.y * s + v.z * c
  elseif axis == 'z' then v.x, v.y = v.x * c - v.y * s, v.x * s + v.y * c
  else v.x, v.z = v.x * c + v.z * s, -v.x * s + v.z * c end
  return v
end
function V.between(a, b) return Vector(b) - Vector(a) end
function V.max(a, b) local ax, ay, az = parts(a); local bx, by, bz = parts(b); return vec(math.max(ax, bx), math.max(ay, by), math.max(az, bz)) end
function V.min(a, b) local ax, ay, az = parts(a); local bx, by, bz = parts(b); return vec(math.min(ax, bx), math.min(ay, by), math.min(az, bz)) end

local C = {}
local CM = {}
__tw_color_meta = CM
local channels = { 'r', 'g', 'b', 'a' }
CM.__index = function(c, k)
  if type(k) == 'number' then return rawget(c, channels[k]) end
  return C[k]
end
CM.__newindex = function(c, k, value)
  if type(k) == 'number' and channels[k] then rawset(c, channels[k], value) else rawset(c, k, value) end
end
local function col(r, g, b, a) return setmetatable({ r = r or 0, g = g or 0, b = b or 0, a = a or 1 }, CM) end
local named = {
  White = { 1, 1, 1 }, Brown = { 0.443, 0.231, 0.09 }, Red = { 0.856, 0.1, 0.094 }, Orange = { 0.956, 0.392, 0.113 },
  Yellow = { 0.905, 0.898, 0.172 }, Green = { 0.192, 0.701, 0.168 }, Teal = { 0.129, 0.694, 0.607 }, Blue = { 0.118, 0.53, 1 },
  Purple = { 0.627, 0.125, 0.941 }, Pink = { 0.96, 0.439, 0.807 }, Grey = { 0.5, 0.5, 0.5 }, Black = { 0.25, 0.25, 0.25 },
}
Color = setmetatable({}, {
  __index = function(_, k)
    local n = named[k]
    if n then return col(n[1], n[2], n[3], 1) end
    return C[k]
  end,
  __call = function(_, r, g, b, a)
    if type(r) == 'table' then return col(rawget(r, 'r') or r[1], rawget(r, 'g') or r[2], rawget(r, 'b') or r[3], rawget(r, 'a') or r[4]) end
    return col(r, g, b, a)
  end,
})
C.new = function(...) return Color(...) end
function C.fromString(name) local n = named[name] or named[(name or ''):gsub('^%l', string.upper)]; if n then return col(n[1], n[2], n[3], 1) end end
function C.fromHex(hex)
  hex = (hex or ''):gsub('#', '')
  local function byte(i) return (tonumber(hex:sub(i, i + 1), 16) or 0) / 255 end
  return col(byte(1), byte(3), byte(5), #hex >= 8 and byte(7) or 1)
end
function C.toHex(c, alpha)
  local function hx(v) return string.format('%02x', math.floor(math.max(0, math.min(1, v)) * 255 + 0.5)) end
  return hx(c.r) .. hx(c.g) .. hx(c.b) .. (alpha and hx(c.a) or '')
end
function C.toString(c, tolerance)
  for name, n in pairs(named) do
    if abs(c.r - n[1]) <= (tolerance or 0.01) and abs(c.g - n[2]) <= (tolerance or 0.01) and abs(c.b - n[3]) <= (tolerance or 0.01) then return name end
  end
end
function C.copy(c) return col(c.r, c.g, c.b, c.a) end
function C.get(c) return c.r, c.g, c.b, c.a end
function C.set(c, r, g, b, a) c.r = r or c.r; c.g = g or c.g; c.b = b or c.b; c.a = a or c.a; return c end
function C.setAt(c, k, value) c[k] = value; return c end
function C.equals(c, o, margin)
  margin = margin or 0.01
  return abs(c.r - (o.r or o[1])) <= margin and abs(c.g - (o.g or o[2])) <= margin and abs(c.b - (o.b or o[3])) <= margin
end
function C.lerp(c, o, t) return col(c.r + (o.r - c.r) * t, c.g + (o.g - c.g) * t, c.b + (o.b - c.b) * t, c.a + ((o.a or 1) - c.a) * t) end
CM.__eq = function(a, b) return C.equals(a, b) end
CM.__tostring = function(c) return string.format('{r=%g, g=%g, b=%g, a=%g}', c.r, c.g, c.b, c.a) end
";
    }
}
