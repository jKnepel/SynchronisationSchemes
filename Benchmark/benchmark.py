import subprocess
import socket
import struct
import time
import sys
import os
import ctypes
from pywinauto import Application

LoadScene = b'\x00'
UnloadScene = b'\x01'
StartHost = b'\x02'
StartClient = b'\x03'
StopHost = b'\x04'
StopClient = b'\x05'
GetBenchmark = b'\x06'
DirectionalInput = b'\x07'
SetObjectNumber = b'\x08'

exe_path = r"../Builds/SynchronisationSchemes.exe"
warmup_runs = 10
runs = 10

def main():
    # change working directory to current file
    script_directory = os.path.dirname(os.path.abspath(__file__)) 
    os.chdir(script_directory)

    # load processes with correct port
    port1 = 9989
    port2 = 9990
    process1 = subprocess.Popen([exe_path, str(port1)])
    process2 = subprocess.Popen([exe_path, str(port2)])
    time.sleep(5)

    # place both processes on main desktop side by side
    screen_width, available_height = get_screen_size()
    app1 = Application().connect(process=process1.pid)
    window1 = app1.top_window()
    window1.set_focus()
    window1.move_window(x=0, y=0, width=screen_width // 2, height=available_height)
    app2 = Application().connect(process=process2.pid)
    window2 = app2.top_window()
    window2.set_focus()
    window2.move_window(x=screen_width // 2, y=0, width=screen_width // 2, height=available_height)

    # establish tcp socket for communicating with benchmark controller
    process1_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    process1_socket.connect(('localhost', port1))
    process2_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    process2_socket.connect(('localhost', port2))
    time.sleep(2)

    # benchmark
    benchmark(process1_socket, process2_socket, 'AuthoritativeSimulation', 10)
    benchmark(process1_socket, process2_socket, 'ClientServerSimulation', 10)

    process1_socket.close()
    process2_socket.close()
    process1.terminate()
    process2.terminate()

def benchmark(process1: socket, process2: socket, scene: str, number_objects: int):
    scene_bytes = scene.encode('utf-8')
    process1.sendall(LoadScene + struct.pack('B', len(scene_bytes)) + scene_bytes)
    process2.sendall(LoadScene + struct.pack('B', len(scene_bytes)) + scene_bytes)
    time.sleep(1) # wait for scene to be loaded

    process1.sendall(SetObjectNumber + struct.pack('B', 4) + struct.pack('I', number_objects))
    process2.sendall(SetObjectNumber + struct.pack('B', 4) + struct.pack('I', number_objects))
    process1.sendall(StartHost + struct.pack('B', 0))
    process2.sendall(StartClient + struct.pack('B', 0))
    time.sleep(1)

    directional_input(process2, 1.0, 0, 2)
    directional_input(process2, 0.8, 0.5, 3)
    directional_input(process2, 0, 0.5, 2)
    directional_input(process2, -0.2, -0.5, 10)

    #response = client_socket.recv(1024)
    #print(f"Received: {response.decode('utf-8')}")
    process1.sendall(UnloadScene + struct.pack('B', 0))
    process2.sendall(UnloadScene + struct.pack('B', 0))
    time.sleep(5)

def get_screen_size():
    user32 = ctypes.windll.user32
    screen_width = user32.GetSystemMetrics(0)
    screen_height = user32.GetSystemMetrics(1)

    # Get the taskbar window handle
    taskbar_hwnd = user32.FindWindowW("Shell_TrayWnd", None)
    rect = ctypes.wintypes.RECT()
    user32.GetWindowRect(taskbar_hwnd, ctypes.byref(rect))
    
    # Calculate the taskbar height
    taskbar_height = rect.bottom - rect.top

    # Calculate available height for the windows
    available_height = screen_height - taskbar_height

    return screen_width, available_height

def directional_input(socket: socket, up: float, right: float, duration: float):
    socket.sendall(DirectionalInput + struct.pack('B', 8) + struct.pack('f', up) + struct.pack('f', right))
    time.sleep(duration)
    socket.sendall(DirectionalInput + struct.pack('B', 8) + struct.pack('f', 0) + struct.pack('f', 0))

if __name__ == "__main__":
    sys.exit(main())