#!/usr/bin/env python3
"""Provide a GTK drag source for the isolated native editor check."""

import ctypes
import sys


def main():
    gtk = ctypes.CDLL("libgtk-3.so.0")
    gobject = ctypes.CDLL("libgobject-2.0.so.0")
    gtk.gtk_init.argtypes = [ctypes.c_void_p, ctypes.c_void_p]
    gtk.gtk_window_new.argtypes = [ctypes.c_int]
    gtk.gtk_window_new.restype = ctypes.c_void_p
    gtk.gtk_window_set_title.argtypes = [ctypes.c_void_p, ctypes.c_char_p]
    gtk.gtk_window_set_default_size.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_int]
    gtk.gtk_button_new_with_label.argtypes = [ctypes.c_char_p]
    gtk.gtk_button_new_with_label.restype = ctypes.c_void_p
    gtk.gtk_container_add.argtypes = [ctypes.c_void_p, ctypes.c_void_p]
    gtk.gtk_widget_show_all.argtypes = [ctypes.c_void_p]
    gtk.gtk_drag_source_set.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_void_p, ctypes.c_int, ctypes.c_int]
    gtk.gtk_selection_data_set.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_int, ctypes.c_char_p, ctypes.c_int]
    gtk.gdk_atom_intern.argtypes = [ctypes.c_char_p, ctypes.c_int]
    gtk.gdk_atom_intern.restype = ctypes.c_void_p
    gobject.g_signal_connect_data.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_void_p,
                                            ctypes.c_void_p, ctypes.c_void_p, ctypes.c_int]
    gobject.g_signal_connect_data.restype = ctypes.c_ulong

    class Target(ctypes.Structure):
        _fields_ = [("target", ctypes.c_char_p), ("flags", ctypes.c_uint), ("info", ctypes.c_uint)]

    mime = sys.argv[1].encode()
    payload = sys.argv[2].encode("utf-8")
    target = Target(mime, 0, 0)
    callback_type = ctypes.CFUNCTYPE(None, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p,
                                    ctypes.c_uint, ctypes.c_uint, ctypes.c_void_p)

    @callback_type
    def provide(widget, context, selection, info, timestamp, user_data):
        gtk.gtk_selection_data_set(selection, gtk.gdk_atom_intern(mime, 0), 8, payload, len(payload))

    gtk.gtk_init(None, None)
    window = gtk.gtk_window_new(0)
    gtk.gtk_window_set_title(window, b"Tangopad drag source")
    gtk.gtk_window_set_default_size(window, 200, 80)
    button = gtk.gtk_button_new_with_label(b"Drag test item")
    gtk.gtk_container_add(window, button)
    gtk.gtk_drag_source_set(button, 1 << 8, ctypes.byref(target), 1, 2)
    gobject.g_signal_connect_data(button, b"drag-data-get", provide, None, None, 0)
    gtk.gtk_widget_show_all(window)
    gtk.gtk_main()


if __name__ == "__main__":
    main()
