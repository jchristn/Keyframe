"""Dataclass base with PascalCase JSON mapping.

Wire JSON uses PascalCase (``MaxSceneMs``); Python fields use snake_case (``max_scene_ms``).
Unknown keys returned by the server are kept in ``extra`` and written back by ``to_dict``,
so a settings round trip never drops fields this SDK version does not know about.
"""

from __future__ import annotations

import dataclasses
import re
import typing
from enum import Enum
from typing import Any, Dict, Union

_SNAKE = re.compile(r"(?<!^)(?=[A-Z])")


def to_pascal(name: str) -> str:
    """Convert ``snake_case`` to ``PascalCase``."""
    return "".join(part[:1].upper() + part[1:] for part in name.split("_"))


def to_snake(name: str) -> str:
    """Convert ``PascalCase`` to ``snake_case``."""
    return _SNAKE.sub("_", name).lower()


def _decode(tp: Any, value: Any) -> Any:
    if value is None:
        return None
    origin = typing.get_origin(tp)
    if origin is Union:
        args = [a for a in typing.get_args(tp) if a is not type(None)]
        return _decode(args[0], value) if args else value
    if origin in (list, typing.List):
        (item,) = typing.get_args(tp) or (Any,)
        return [_decode(item, v) for v in value] if isinstance(value, list) else value
    if origin in (dict, typing.Dict):
        args = typing.get_args(tp)
        vt = args[1] if len(args) == 2 else Any
        return {k: _decode(vt, v) for k, v in value.items()} if isinstance(value, dict) else value
    if isinstance(tp, type):
        if issubclass(tp, Model) and isinstance(value, dict):
            return tp.from_dict(value)
        if issubclass(tp, Enum):
            try:
                return tp(value)
            except ValueError:
                return value
        if tp is float and isinstance(value, int) and not isinstance(value, bool):
            return float(value)
    return value


def encode(value: Any, omit_none: bool = False) -> Any:
    """Convert models, enums, lists, and dicts into JSON-ready values."""
    if isinstance(value, Model):
        return value.to_dict(omit_none=omit_none)
    if isinstance(value, Enum):
        return value.value
    if isinstance(value, list):
        return [encode(v, omit_none) for v in value]
    if isinstance(value, tuple):
        return [encode(v, omit_none) for v in value]
    if isinstance(value, dict):
        return {k: encode(v, omit_none) for k, v in value.items() if not (omit_none and v is None)}
    return value


class Model:
    """Base for every wire model. Subclasses are dataclasses with an ``extra`` field."""

    @classmethod
    def from_dict(cls, data: Dict[str, Any]) -> Any:
        """Build an instance from a PascalCase JSON object."""
        if data is None:
            return None
        hints = typing.get_type_hints(cls)
        kwargs: Dict[str, Any] = {}
        known = set()
        for f in dataclasses.fields(cls):  # type: ignore[arg-type]
            if f.name == "extra":
                continue
            wire = f.metadata.get("wire", to_pascal(f.name))
            known.add(wire)
            if wire in data:
                kwargs[f.name] = _decode(hints[f.name], data[wire])
        instance = cls(**kwargs)
        extra = {k: v for k, v in data.items() if k not in known}
        if extra:
            instance.extra = extra  # type: ignore[attr-defined]
        return instance

    def to_dict(self, omit_none: bool = False) -> Dict[str, Any]:
        """Serialize to a PascalCase JSON object. ``omit_none`` drops null members."""
        out: Dict[str, Any] = {}
        for f in dataclasses.fields(self):  # type: ignore[arg-type]
            if f.name == "extra":
                continue
            value = getattr(self, f.name)
            if omit_none and value is None:
                continue
            out[f.metadata.get("wire", to_pascal(f.name))] = encode(value, omit_none)
        extra = getattr(self, "extra", None)
        if extra:
            for k, v in extra.items():
                out.setdefault(k, v)
        return out
