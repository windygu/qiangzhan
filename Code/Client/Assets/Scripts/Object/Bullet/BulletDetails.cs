using System.Collections;
using UnityEngine;

#region FlyControllers
/// <summary>
/// 子弹飞行轨迹的控制器.
/// </summary>
public abstract class BulletFlyController
{
	protected Bullet mBullet = null;

	/// <summary>
	/// 构造函数中只记录参数值. 实际初始化操作放到Launch中, 在Launch中, 才能保证子弹的已经准备就绪.
	/// </summary>
	/// <param name="bullet"></param>
	public BulletFlyController(Bullet bullet)
	{
		mBullet = bullet;
	}

	/// <summary>
	/// 子弹对象已经准备完毕时调用. 用来准备飞行控制器.
	/// </summary>
	public virtual void Launch() { }

	/// <summary>
	/// 飞行更新, 如果返回false, 表示子弹需要进入到达状态.
	/// </summary>
	public virtual bool FlyUpdate(uint elapsed)
	{
		if (!CanAccelerate)
			return true;

		if (mBullet.AccelerateDelay > elapsed)
			mBullet.AccelerateDelay -= elapsed;
		else
		{
			mBullet.AccelerateDelay = 0;
			mBullet.FlySpeed += elapsed * mBullet.Accelerate / 1000f;
		}
		
		return true;
	}

	/// <summary>
	/// 是否应用加速度.
	/// </summary>
	protected virtual bool CanAccelerate { get { return true; } }

	/// <summary>
	/// 是否在2D上直线飞行(即在(x,z)平面上, 不转向).
	/// </summary>
	/// <remarks>
	/// <para>如果并非沿直线飞行, 在Update进入下一个位置之前, 需要检测场景内的阻挡.</para>
	/// 直线飞行的子弹在开始飞行之前就已经计算完成, 因此无需检测.
	/// </remarks>
	public abstract bool FlyStraight { get; }

	/// <summary>
	/// 求f(x) = ax^2 + bx + c的非负数解(b > 0).
	/// </summary>
	protected float solveQuadEquation(float a, float b, float c)
	{
		if (Utility.isZero(a))
			return (-c / b);

		float delta = b * b - 4 * a * c;
		return (delta >= 0f) ? ((-b + Mathf.Sqrt(delta)) / (2 * a)) : float.NaN;
	}

	/// <summary>
	/// 子弹在2D方向上, 从oldPosition, 沿当前朝向, 飞行elapsed毫秒.
	/// 传出实际有效的飞行时间(elapsed), 飞行的距离(flyDistance)以及飞行到的位置(newPosition).
	/// </summary>
	/// <returns>如果返回false, 表示子弹已经飞行了足够的距离, 需要进行爆炸</returns>
	protected bool twoDimensionFly(Vector3 oldPosition, ref uint elapsed, ref float flyDistance, ref Vector3 newPosition)
	{
		newPosition = oldPosition;

		if (!computeFlyDistance(ref elapsed, ref flyDistance))
			return false;

		// 2D方向上的下一个位置.
		newPosition += (mBullet.FlyDirection * Vector3.forward) * flyDistance;

		return true;
	}

	/// <summary>
	/// <para>计算子弹经过elapsed时间飞行的距离.</para>
	/// 当子弹按照速度飞行的距离 > 子弹尚可以飞行的距离时, 子弹只有一部分的飞行时间是有效的, elapsed会被修改.
	/// </summary>
	/// <returns>如果返回false, 表示子弹已经飞行了足够的距离, 需要进行爆炸</returns>
	protected bool computeFlyDistance(ref uint elapsed, ref float flyDistance)
	{
		if (mBullet.LeftRange <= 0)
			return false;

		float a = (CanAccelerate && mBullet.AccelerateDelay == 0) ? mBullet.Accelerate : 0f;
		float seconds = elapsed / 1000f;
		flyDistance = seconds * mBullet.FlySpeed + a * seconds * seconds / 2;

		mBullet.LeftRange -= flyDistance;
		if (mBullet.LeftRange <= 0)
		{
			flyDistance += mBullet.LeftRange;
			mBullet.LeftRange = -1f;

			// 只有flyDistance的飞行是有效的, 那么, 有效的飞行时间也需要重新计算.
			elapsed = (uint)(solveQuadEquation(a / 2f, mBullet.FlySpeed, -flyDistance) * 1000f);
		}

		return true;
	}
}

/// <summary>
/// <para>子弹按照函数图像飞行.</para>
/// 图像的Y轴, 对应着最终子弹飞行轨迹的Z轴.
/// </summary>
public abstract class BulletPathEquation
{
	public BulletPathEquation() 
	{
	}
	/// <summary>
	/// 子弹的射程.
	/// </summary>
	public float TotalRange { get; set; }

	/// <summary>
	/// 获取子弹在当前位置(currentPosition), 飞行一定距离(distance)之后的新位置.
	/// </summary>
	public abstract Vector3 NextPosition(Vector3 currentPosition, float distance);
}

/// <summary>
/// 沿圆弧运动.
/// </summary>
public class BulletPathEquationCircularArc : BulletPathEquation
{
	/// <summary>
	/// 圆周的半径.
	/// </summary>
	readonly float Radius;

	/// <summary>
	/// 圆心.
	/// </summary>
	readonly Vector3 CircleCenter = Vector3.zero;

	/// <summary>
	/// 旋转方式为顺时针(1f)/逆时针(-1f).
	/// </summary>
	readonly float ClockwiseMultiplier = 0f;

	/// <summary>
	/// x^2 + (y - R)^2 = R^2.
	/// </summary>
	public BulletPathEquationCircularArc(float radius, bool clockwise)
	{
		CircleCenter.z = Radius = radius;
		ClockwiseMultiplier = clockwise ? 1f : -1f;
		if (radius <= 0f)
			throw new System.ArgumentException("invalid parameter for equation x^2 + (y - R)^2 = R^2.");
	}

	/// <summary>
	/// 根据圆弧的长度, 计算子弹沿圆周飞行的角度, 从而获取圆周上的下一点.
	/// </summary>
	public override Vector3 NextPosition(Vector3 currentPosition, float distance)
	{
		return Utility.RotateVectorByRadian(currentPosition, ClockwiseMultiplier * distance / Radius, CircleCenter);
	}
}

/// <summary>
/// 沿正弦曲线.
/// </summary>
/// <remarks>
/// <para>如果需要精确的按照正弦曲线飞行, 需要得到给定正弦曲线长度, 返回正弦曲线坐标的方法(需要计算积分).</para>
/// <para>下面的方法简单的模拟了这一点:</para> 
/// <para>在计算NextPosition时,</para>
/// <para>假设当前点为(x, y), 新的点为(x', y'), dx = x' - x, dy = y' - y, d = dy / dx.</para>
/// <para>得到, dy = d * dx	(1)</para>
/// <para>将这一帧飞行的长度为L的曲线, 视为一小段长度为R = L的线段, 得到:</para>
/// <para>R^2 = dx^2 + dy^2.	(2)</para>
/// <para>联立(1)和(2), 得到dx = R / sqrt(1 + d^2).</para>
/// 所以, x' = x + dx, 再根据f(x)求y'(确保结果在曲线上)即可.
/// </remarks>
public class BulletPathEquationSineWave : BulletPathEquation
{
	readonly float A = 1f, B = 1f;
	/// <summary>
	/// y = Asin(Bx).
	/// </summary>
	public BulletPathEquationSineWave(float a, float b)
	{
		A = a;
		B = b;
		if (a == 0f || b == 0f)
			throw new System.ArgumentException("invalid parameter for equation y = Asin(Bx).");
	}

	/// <summary>
	/// 正弦函数在x处的导数.
	/// </summary>
	float derivative(float x)
	{
		return A * B * Mathf.Cos(B * x);
	}

	/// <summary>
	/// 获取f(x)值.
	/// </summary>
	float f(float x) {
		return A * Mathf.Sin(B * x);
	}

	public override Vector3 NextPosition(Vector3 currentPosition, float distance)
	{
		float k = derivative(currentPosition.x);

		currentPosition.x += distance / Mathf.Sqrt(1 + k * k);

		// 根据方程重新计算y值, 使每次的点都落在曲线上.
		currentPosition.z = f(currentPosition.x);

		return currentPosition;
	}
}

/// <summary>
/// <para>根据函数, 自定义飞行轨迹</para>
/// 可能抛出Exception.
/// </summary>
public class BulletFlyAlongEquationGraph : BulletFlyController
{
	/// <summary>
	/// 子弹的起始位置.
	/// </summary>
	readonly Vector3 StartPosition;

	/// <summary>
	/// 坐标轴旋转角度的正弦值(角度为从x正半轴开始, 逆时针方向).
	/// </summary>
	readonly float RotationSin = 0f;
	/// <summary>
	/// 坐标轴旋转角度的余弦值(角度为从x正半轴开始, 逆时针方向).
	/// </summary>
	readonly float RotationCos = 0f;

	/// <summary>
	/// 轨迹方程.
	/// </summary>
	BulletPathEquation mPathEquation = null;

	public BulletFlyAlongEquationGraph(Bullet bullet, BulletPathEquation equation)
		: base(bullet)
	{
		mPathEquation = equation;

		StartPosition = bullet.StartPosition;

		// 转换到通用的坐标系中.
		float radian = (90f - mBullet.FlyDirection.eulerAngles.y) * Mathf.Deg2Rad;
		RotationSin = Mathf.Sin(radian);
		RotationCos = Mathf.Cos(radian);
	}

	public override bool FlyStraight
	{
		get { return false; }
	}

	public override void Launch()
	{
		mPathEquation.TotalRange = mBullet.TotalRange;
		base.Launch();
	}

	public override bool FlyUpdate(uint elapsed)
	{
		float distance = 0f;

		// 计算这一帧飞行的距离.
		if (!computeFlyDistance(ref elapsed, ref distance))
			return false;

		Vector3 currentPosition = mBullet.GetPosition();

		// 将坐标系旋转到标准坐标系. 以子弹的飞行方向为x的正半轴方向.
		Vector3 nextPosition = RotateAxis(currentPosition - StartPosition);
		// 在x, y坐标系上计算下一点.
		nextPosition = mPathEquation.NextPosition(nextPosition, distance);
		// 恢复坐标系为unity坐标系.
		nextPosition = RestoreAxis(nextPosition) + StartPosition;

		// 检测中途的阻挡.
		if (mBullet.Scene.TestLineBlock(currentPosition, nextPosition, true, true, out nextPosition))
		{
			nextPosition.y = currentPosition.y;
			mBullet.SetPosition(nextPosition);
			return false;
		}

		// 检查碰撞命中.
		bool need2Explode = mBullet.FlyHit(currentPosition, distance, mBullet.GetDirection());
		if (mBullet.LeftRange <= 0 || need2Explode)
		{
			need2Explode = true;
		}

		mBullet.SetPosition(nextPosition);

		// 令子弹面朝速度方向.
		nextPosition -= currentPosition;
		if (nextPosition != Vector3.zero)
			mBullet.SetRotation(Quaternion.LookRotation(nextPosition));

		return !need2Explode;
	}

	Vector3 RotateAxis(Vector3 position)
	{
		position.Set(position.x * RotationCos + position.z * RotationSin, 
			position.y,
			position.z * RotationCos - position.x * RotationSin
			);
		return position;
	}

	Vector3 RestoreAxis(Vector3 position)
	{
		position.Set(position.x * RotationCos - position.z * RotationSin, 
			position.y, 
			position.z * RotationCos + position.x * RotationSin
			);
		return position;
	}
}

/// <summary>
/// 水平直线飞行的轨迹控制器.
/// </summary>
public class BulletFlyAlongHorizontalLine : BulletFlyController
{
	public BulletFlyAlongHorizontalLine(Bullet bullet)
		: base(bullet)
	{
	}

	public override bool FlyStraight
	{
		get { return true; }
	}

	public override bool FlyUpdate(uint elapsed)
	{
		Vector3 from = mBullet.GetPosition();
		float moveDistance = 0;
		Vector3 nextPosition = Vector3.zero;

		if (!twoDimensionFly(from, ref elapsed, ref moveDistance, ref nextPosition))
			return false;

		bool need2Explode = mBullet.FlyHit(from, moveDistance, mBullet.GetDirection());

		if (mBullet.LeftRange <= 0 || need2Explode)
		{
			need2Explode = true;
		}

		//向前移动 moveDistance 距离
		mBullet.SetPosition(nextPosition);

		base.FlyUpdate(elapsed);

		return !need2Explode;
	}
}

/// <summary>
/// 子弹从起始点, 沿斜线, 朝向目标点飞行.
/// </summary>
public class BulletFlyAlongDiagonalLine : BulletFlyController
{
	public BulletFlyAlongDiagonalLine(Bullet bullet)
		: base(bullet)
	{
	}

	public override bool FlyStraight
	{
		get { return true; }
	}

	public override bool FlyUpdate(uint elapsed)
	{
		Vector3 from = mBullet.GetPosition();
		float moveDistance = 0;
		Vector3 nextPosition = Vector3.zero;

		if (!twoDimensionFly(from, ref elapsed, ref moveDistance, ref nextPosition))
		{
			nextPosition.y = mBullet.Scene.GetHeight(nextPosition.x, nextPosition.z);
			mBullet.SetPosition(nextPosition);
			return false;
		}

		bool need2Explode = mBullet.FlyHit(from, moveDistance, mBullet.GetDirection());

		float heightOffset = mBullet.LeftRange * (mBullet.StartPosition.y - mBullet.TargetPosition.y) / mBullet.TotalRange;
		nextPosition.y = heightOffset + mBullet.Scene.GetHeight(nextPosition.x, nextPosition.z);

		if (mBullet.LeftRange <= 0 || need2Explode)
		{
			need2Explode = true;
		}

		//向前移动 moveDistance 距离
		mBullet.SetPosition(nextPosition);
		nextPosition -= from;

		// 改变子弹的朝向, 为速度矢量的方向.
		if (nextPosition != Vector3.zero)
			mBullet.SetRotation(Quaternion.LookRotation(nextPosition));

		base.FlyUpdate(elapsed);
		return !need2Explode;
	}
}

/// <summary>
/// 垂直下降的子弹的飞行控制器.
/// </summary>
public class BulletFlyAlongVerticalLine : BulletFlyController
{
	public BulletFlyAlongVerticalLine(Bullet bullet)
		: base(bullet)
	{
	}

	public override bool FlyStraight
	{
		get { return false; }
	}

	public override bool FlyUpdate(uint elapsed)
	{
		float seconds = elapsed / 1000f;
		float a = (mBullet.AccelerateDelay == 0) ? mBullet.Accelerate : 0f;
		float offset = mBullet.FlySpeed * seconds + a * seconds * seconds / 2f;

		bool need2Explode = false;

		Vector3 nextPosition = mBullet.GetPosition();

		float horizon = mBullet.Scene.GetHeight(nextPosition.x, nextPosition.z);
		if ((nextPosition.y -= offset) <= horizon)
		{
			nextPosition.y = horizon;
			need2Explode = true;
		}

		mBullet.SetPosition(nextPosition);

		base.FlyUpdate(elapsed);
		return !need2Explode;
	}
}

/// <summary>
/// 抛物线轨迹飞行的控制器.
/// </summary>
public class BulletFlyAlongParabola : BulletFlyController
{
	public BulletFlyAlongParabola(Bullet bullet, float g)
		: base(bullet)
	{
		G = g;
	}

	public override void Launch()
	{
		Vector3 currentPosition = mBullet.StartPosition;
		float heightOffset = currentPosition.y - mBullet.Scene.GetHeight(currentPosition.x, currentPosition.z);
		speedVertical = initialSpeedY(mBullet.LeftRange,
			heightOffset, mBullet.FlySpeed, mBullet.Accelerate, mBullet.AccelerateDelay);

		base.Launch();
	}

	public override bool FlyStraight
	{
		get { return false; }
	}

	public override bool FlyUpdate(uint elapsed)
	{
		Vector3 from = mBullet.GetPosition();
		float moveDistance = 0;
		Vector3 nextPosition = Vector3.zero;

		if (!twoDimensionFly(from, ref elapsed, ref moveDistance, ref nextPosition))
		{
			nextPosition.y = mBullet.Scene.GetHeight(nextPosition.x, nextPosition.z);
			mBullet.SetPosition(nextPosition);
			return false;
		}

		float seconds = elapsed / 1000f;

		float heightOffset = speedVertical * seconds - seconds * seconds * G / 2;

		speedVertical -= seconds * G;

		nextPosition.y += heightOffset;

		//如果碰到地面  爆炸
		float horizon = mBullet.Scene.GetHeight(nextPosition.x, nextPosition.z);

		bool need2Explode = false;

		if (nextPosition.y <= horizon || mBullet.LeftRange <= 0)
		{
			nextPosition.y = horizon;
			need2Explode = true;
		}

		//向前移动 moveDistance 距离
		mBullet.SetPosition(nextPosition);

		nextPosition -= from;

		// 改变速度的朝向为速度矢量的方向.
		if (nextPosition != Vector3.zero)
			mBullet.SetRotation(Quaternion.LookRotation(nextPosition));

		base.FlyUpdate(elapsed);

		return !need2Explode;
	}

	float speedVertical { get; set; }
	float G { get; set; }

	/// <summary>		
	/// 计算子弹的垂直初速度, 使得子弹按照这个垂直做类抛物线(如果水平为匀速则为抛物线), 可使水平飞到目的地时, 子弹垂直方向恰好到达地面.
	/// </summary>
	/// <param name="distance">子弹水平方向飞行的距离</param>
	/// <param name="initialSpeedX">弹水平方向的初速度</param>
	/// <param name="acceleration">子弹水平方向的加速度</param>
	private float initialSpeedY(float distance, float initialHeight, float initialSpeedX, float acceleration, float accelerateDelay)
	{
		// 子弹飞行的总时间.
		float totalTime = 0;

		// 如果在加速度延迟时间内, 就可以飞到目的地.
		if (initialSpeedX * accelerateDelay >= distance)
			totalTime = distance / initialSpeedX;
		else
		{
			// let ad = accelerateDelay.
			// t' = t - ad, 即, 加速度延迟之后飞行的时间.
			// distance = initialSpeedX * ad + initialSpeedX * t' + (acceleration * t' * t') / 2.
			// (acceleration * t' * t') / 2 + initialSpeedX * t' + initialSpeedX * ad - distance = 0.
			// 二次方程必然有解, 因为(initialSpeedX * ad - distance) * accleration / 2 <= 0,
			// 使得delta >= 0.
			totalTime = accelerateDelay / 1000f;
			totalTime += solveQuadEquation(acceleration / 2f, initialSpeedX, initialSpeedX * accelerateDelay - distance);
		}
		// 从高度initialHeight开始, 做类抛物线(如果水平方向上的加速度不为0, 那么, 形成的不是抛物线,
		// 但是竖直方向始终是带有加速度延迟的匀加速运动).
		// 为保证子弹水平飞行的时间和子弹做类抛物线飞行的时间相等:
		// 对垂直方向上的初速度, 也就是结果, v2而言, 令t1为垂直方向向上飞行的时间:
		// v2 = g*t1								(1)
		// 子弹具有初始的高度, initialHeight, 计算从这个高度到落地子弹下降的时间t3:
		// v2*t3 + (g*(t3)^2) / 2 = initialHeight.	(2)
		// t3是虚拟出来的, 实际目的是为了计算子弹下降的时间t2.
		// t2 = t1 + t3.							(3)
		// t = t1 + t2.								(4)
		// 联立(3)和(4):
		// totalTime = 2*t1 + t3					(5)
		// 联立(1), (2), (5)得:
		// v2 = g*t1
		// v2*t3 + (g*(t3)^2) / 2 = initialHeight
		// totalTime = 2*t1 + t3
		// 化简后, 解方程:
		// t3 = totalTime - 2*v2 / g.
		// (g*(t3)^2) / 2 + v2*t3 = initialHeight
		// 如果t3 = 0, 表示子弹是从水平面起飞的.
		return Utility.isZero(totalTime)
			? float.NegativeInfinity
			: (G * totalTime / 2 - initialHeight / totalTime);
	}
}

/// <summary>
/// 追踪目标的子弹的轨迹控制器.
/// </summary>
public class BulletFlyTrackingTarget : BulletFlyController
{
	const float RADIUS_SCALE = 0.8f;
	/// <summary>
	/// 追踪弹的锁敌半径.
	/// </summary>
	const float MissileSearchRadius = 100f;
	/// <summary>
	/// 追踪弹的默认追踪半径.
	/// </summary>
	private static readonly float DefaultRadius = GameConfig.HomingMissileDefaultRadius;

	public BulletFlyTrackingTarget(Bullet bullet, uint trackDelay, uint trackDuration)
		: base(bullet)
	{
		TrackDelay = trackDelay;
		TrackDuration = trackDuration;
		TrackStarted = false;
	}

	public override bool FlyStraight
	{
		get { return false; }
	}

	public override void Launch()
	{
		TargetID = uint.MaxValue;

		BattleUnit target = searchTarget();

		if (target != null)
		{
			TargetLocation = target.GetPosition();
			TargetID = target.InstanceID;
		}

		base.Launch();
	}

	/// <summary>
	/// <para>搜索离当前位置最近的敌人.</para>
	/// 只在开始时搜索一次, 不会因为中途目标无效而重新搜索.
	/// </summary>
	/// <returns></returns>
	private BattleUnit searchTarget()
	{
		BaseScene scn = SceneManager.Instance.GetCurScene();
		if (scn == null)
			return null;

		Vector3 bulletPosition = mBullet.GetPosition();
		Vector2f myPosition = new Vector2f(bulletPosition.x, bulletPosition.z);

		ArrayList objs = scn.SearchBattleUnit(myPosition, MissileSearchRadius);

		float minDistSquared = float.MaxValue;

		BattleUnit firer = mBullet.FirerAttr.CheckedAttackerObject();
		BattleUnit target = null;
		for (int i = 0; firer != null && i < objs.Count; ++i)
		{
			BattleUnit battleunit = objs[i] as BattleUnit;
			if (battleunit != null && firer.IsEnemy(battleunit))
			{
				float currentDistanceSquared = Utility.Distance2DSquared(battleunit.GetPosition(), bulletPosition);
				if (currentDistanceSquared < minDistSquared)
				{
					minDistSquared = currentDistanceSquared;
					target = battleunit;
				}
			}
		}

		return target;
	}

	/// <summary>
	/// 追踪延迟.
	/// </summary>
	uint TrackDelay { get; set; }

	/// <summary>
	/// 追踪持续时间.
	/// </summary>
	uint TrackDuration { get; set; }

	/// <summary>
	/// <para>飞行时的目标ID.</para>
	/// 如果导弹没有加速度, 导弹一直追踪目标, 直至目标失效或者导弹达到射程.
	/// 如果导弹有加速度, 那么, 导弹按照原速度调整方向至与目标方向一致, 此过程不会加速,
	/// 当目标失效或者导弹已经面朝目标的方向, 那么子弹开始沿直线飞行, 并应用加速度.
	/// </summary>
	uint TargetID { get; set; }

	/// <summary>
	/// 目标在上一帧时的位置. 通过保存该位置, 从而确定目标的位置是否已经发生改变, 确定
	/// 导弹是否需要重新计算追踪半径.
	/// </summary>
	Vector3 TargetLocation { get; set; }

	/// <summary>
	/// 导弹的追踪半径.
	/// </summary>
	float TurnRadius { get; set; }

	bool TrackStarted { get; set; }

	/// <summary>
	/// 只有当目标无效时, 才进行加速.
	/// </summary>
	protected override bool CanAccelerate { get { return TargetID == uint.MaxValue; } }

	public override bool FlyUpdate(uint elapsed)
	{
		Vector3 from = mBullet.GetPosition();
		float moveDistance = 0;
		Vector3 nextPosition = Vector3.zero;

		// 朝当前方向, 水平飞行.
		if (!twoDimensionFly(from, ref elapsed, ref moveDistance, ref nextPosition))
			return false;

		// 追踪持续时间.
		if (TrackStarted && TrackDuration != uint.MaxValue)
		{
			if (TrackDuration > elapsed)
				TrackDuration -= elapsed;
			else
			{
				TrackDuration = uint.MaxValue;
				disableTrack();
			}
		}

		// 检测中途的阻挡.
		if (mBullet.Scene.TestLineBlock(from, nextPosition, true, true, out nextPosition))
		{
			nextPosition.y = from.y;

			mBullet.SetPosition(nextPosition);
			return false;
		}

		// 检测碰撞爆炸.
		bool need2Explode = mBullet.FlyHit(from, moveDistance, mBullet.GetDirection());

		if (mBullet.LeftRange <= 0)
		{
			need2Explode = true;
		}

		mBullet.SetPosition(nextPosition);

		if (TargetID != uint.MaxValue)
		{
			if (TrackDelay > elapsed)
				TrackDelay = TrackDelay - elapsed;
			else
			{
				TrackDelay = 0;
				TrackStarted = true;
				// 时刻根据ID来获取目标, 从而判断目标是否依然有效.
				BattleUnit target = mBullet.Scene.FindObject(TargetID) as BattleUnit;
				if (target != null && target.isAlive())
					trackTarget(target, elapsed);
				else
					disableTrack();
			}
		}
		
		base.FlyUpdate(elapsed);

		return !need2Explode;
	}

	/// <summary>
	/// 关闭追踪.
	/// </summary>
	private void disableTrack()
	{
		TargetID = uint.MaxValue;
	}

	/// <summary>
	/// 追踪目标target.
	/// </summary>
	private void trackTarget(BattleUnit target, uint elapsed)
	{
		Vector3 myPosition = mBullet.GetPosition(), targetPosition = target.GetPosition();

		myPosition.y = targetPosition.y = 0f;

		Quaternion myRotation = mBullet.GetRotation();
		Quaternion targetRotation = Quaternion.LookRotation(targetPosition - myPosition);
		float angleBetween = Quaternion.Angle(myRotation, targetRotation);

		// 面向, 无需追踪.
		if (Utility.isZero(angleBetween))
		{
			// 如果导弹有加速度, 那么当导弹面朝目标时, 开始加速直线飞行.
			if (!Utility.isZero(mBullet.Accelerate))
				disableTrack();
			return;
		}

		// 目标位置发生变化, 重新计算追踪半径.
		if (TargetLocation != targetPosition)
		{
			TargetLocation = targetPosition;
			float maxRadius = computeMaxRadius(myPosition.x, myPosition.z, targetPosition.x,
				targetPosition.z, mBullet.GetDirection());

			if (float.IsNaN(maxRadius))
			{
				return;
			}

			TurnRadius = maxRadius;

			// 当前半径过大, 需要调整.
			if (TurnRadius >= maxRadius)
			{
				TurnRadius = Mathf.Min(maxRadius * RADIUS_SCALE, DefaultRadius);
			}
		}

		// 角速度.
		float damping = Mathf.Rad2Deg * mBullet.FlySpeed / TurnRadius;
		Quaternion q = Quaternion.RotateTowards(myRotation, targetRotation, Mathf.Min(damping * elapsed / 1000f, angleBetween));

		mBullet.FlyDirection = q;
		mBullet.SetRotation(q);
	}

	/// <summary>
	/// <para>计算子弹进行匀速圆周运动时的最大半径.</para>
	/// 子弹必须以不大于这个值的半径飞行, 不然会持续绕目标飞行而不会命中.
	/// </summary>
	float computeMaxRadius(float x1, float y1, float x2, float y2, float alpha)
	{
		LineHelper e1 = new LineHelper();
		e1.fromPoints(x1, y1, x2, y2);

		//Vector3 tmp = Utility.MoveVector3Towards(new Vector3(x1, 0, y1), alpha, 1f);
		//tmp -= new Vector3(x1, 0f, y1);
		//alpha = (float)Mathf.Atan2(tmp.z, tmp.x);

		LineHelper e2 = new LineHelper();
		if (alpha > Mathf.PI)
		{
			alpha -= Mathf.PI * 2f;
		}

		e2.fromSlopeAndPoint(Mathf.PI / 2f - alpha, x1, y1);

		Vector2 c = Vector2.zero;
		if (!e1.crossPoint(out c, e2))
		{
			return float.NaN;
		}

		//float dbg1 = (c - (new Vector2(x1, y1))).sqrMagnitude;
		//float dbg2 = (c - (new Vector2(x2, y2))).sqrMagnitude;
		//if (!Utility.isZero(dbg1 - dbg2))
		//{
		//    ErrorHandler.Parse(ErrorCode.LogicError, "");
		//}

		return (c - (new Vector2(x1, y1))).magnitude;
	}
}

internal class LineHelper
{
	/// <summary>
	/// 直线方程用ay = kx + b的方式表示, 其中a取值0或1, k和b为任意实数.
	/// 这样可以处理与坐标轴平行的直线与其它直线的交点. 
	/// </summary>
	private int a = 0;
	private float k = 0f;
	private float b = 0f;

	/// <summary>
	/// 构造过(x1, y1)和(x2, y2)的线段的中垂线.
	/// </summary>
	public void fromPoints(float x1, float y1, float x2, float y2)
	{
		if (Utility.isZero(y2 - y1))	// x = c的形式.
		{
			a = 0;
			k = -1;
			b = (x1 + x2) / 2;
		}
		else if (Utility.isZero(x2 - x1))	// y = c
		{
			a = 1;
			k = 0;
			b = (y1 + y2) / 2;
		}
		else	// y = kx + b
		{
			a = 1;
			k = -1 * (x2 - x1) / (y2 - y1);
			b = (y1 + y2) / 2 - k * (x1 + x2) / 2;
		}
	}

	/// <summary>
	/// 构造斜率为tan(alpha)且过(x1, y1)的直线的垂线.
	/// </summary>
	public void fromSlopeAndPoint(float alpha, float x1, float y1)
	{
		if (Utility.isInteger(alpha / Mathf.PI))	// tan(alpha) = 0, 与x平行.
		{
			a = 0;
			k = -1;
			b = x1;
		}
		else if (Utility.isInteger(alpha * 2 / Mathf.PI))	// 与y平行.
		{
			a = 1;
			k = 0;
			b = y1;
		}
		else
		{
			a = 1;
			k = -1 / Mathf.Tan(alpha);
			b = y1 - k * x1;
		}
	}

	/// <summary>
	/// 求两条直线的交点.
	/// 如果存在, 保存在point中, 并返回true; 否则为false, point为zero.
	/// </summary>
	public bool crossPoint(out Vector2 point, LineHelper other)
	{
		point = Vector2.zero;

		float d = other.a * k - a * other.k;
		// 两条直线无交点.
		if (Utility.isZero(d))
		{
			return false;
		}

		float x = (a * other.b - other.a * b) / d;
		float y = (a == 0) ? (other.k * x + other.b) : (k * x + b);

		point.Set(x, y);
		return true;
	}
}

#endregion FlyControllers

#region ExplodeControllers
/// <summary>
/// 子弹爆炸的控制器.
/// </summary>
public abstract class BulletExplodeController
{
	protected Bullet mBullet = null;
	public BulletExplodeController(Bullet bullet)
	{
		mBullet = bullet;
	}
	public abstract void Explode();
}

/// <summary>
/// 爆炸时的表现, 与碰撞相似(见"技能表格说明/bullettypedetails").
/// </summary>
public class BulletExplodeByCollision : BulletExplodeController
{
	public BulletExplodeByCollision(Bullet bullet)
		: base(bullet)
	{
	}

	public override void Explode()
	{
		ErrorHandler.Parse(
			SkillDetails.CreateCreationAround(mBullet.FirerAttr, mBullet.CreationOnArrive, mBullet.GetPosition(), mBullet.GetDirection()),
			"failed to create creature on bullet arrived"
			);

		// 爆炸特效, 仅在子弹到达终点时播放, 即, 子弹没有命中足够的人.
		if (mBullet.CurrentHittedCount < mBullet.MaxHitCount)
			SkillClientBehaviour.AddSceneEffect(mBullet.ExplodeEffect, mBullet.GetPosition(), mBullet.GetDirection());

		// 用碰撞来模拟爆炸, 从而使得技能效果的方向沿着子弹的飞行方向.
		if (mBullet.TargetSelectionOnArrive != uint.MaxValue && mBullet.SkillEffectOnArrive != uint.MaxValue)
		{
			TargetSelectionTableItem targetSel = DataManager.TargetSelectionTable[mBullet.TargetSelectionOnArrive] as TargetSelectionTableItem;

			ArrayList targets = SkillUtilities.SelectTargets(mBullet.FirerAttr, mBullet.GetPosition(), mBullet.GetDirection(), targetSel);

			AttackerAttr other = mBullet.FirerAttr;
			other.SetEffectStartLocation(mBullet.GetPosition(), mBullet.GetDirection());

			foreach (BattleUnit t in targets)
			{
				SkillDetails.AddSkillEffectByResource(other, t, mBullet.SkillEffectOnArrive);
			}
		}
	}
}

/// <summary>
/// 常规的爆炸.
/// </summary>
public class BulletExplodeNormally : BulletExplodeController
{
	public BulletExplodeNormally(Bullet bullet)
		: base(bullet)
	{
	}

	public override void Explode()
	{
		ErrorHandler.Parse(
			SkillDetails.CreateCreationAround(mBullet.FirerAttr, mBullet.CreationOnArrive, mBullet.GetPosition(),mBullet.GetDirection()),
			"failed to create creature on bullet arrived"
			);

		SkillClientBehaviour.AddSceneEffect(mBullet.ExplodeEffect, mBullet.GetPosition(), mBullet.GetDirection());

		// 检查该种子弹是否可以产生爆炸伤害.
		SkillDetails.SelectTargetAndAddSkillEffect(
			mBullet.FirerAttr, mBullet.GetPosition(), mBullet.GetDirection(),
			mBullet.TargetSelectionOnArrive,
			mBullet.SkillEffectOnArrive
		);
	}
}

public class BulletExplodeSplit : BulletExplodeController
{
	public BulletExplodeSplit(Bullet bullet)
		: base(bullet)
	{ 
	}

	public override void Explode()
	{
		BaseScene scene = SceneManager.Instance.GetCurScene();
		if (scene != null)
			split(scene, mBullet);
	}

	private void split(BaseScene scene, Bullet oldBullet)
	{
	}
}

#endregion ExplodeControllers

#region Helpers
/// <summary>
/// 在子弹创建之前, 根据子弹的类型, 调整子弹的目标点位置.
/// </summary>
public static class BulletAdjuster
{
	/// <summary>
	/// 根据子弹的类型, 调整子弹的目标点.
	/// </summary>
	public static Vector3 AdjustBulletTargetPosition(
		BulletTableItem bulletRes,
		Vector3 startPosition,
		Vector3 targetPosition
		)
	{
		float dir = Utility.Vector3ToRadian(targetPosition - startPosition);
		
		// 几种类型的子弹, 目标点延伸到最大射程处.
		return NeedAdjustTargetPosition(bulletRes.type)
			? Utility.MoveVector3Towards(startPosition, dir, bulletRes.flyRange)
			: targetPosition;
	}

	/// <summary>
	/// 根据子弹的类型, 处理子弹的飞行方向.
	/// 返回子弹新的飞行目标点.
	/// </summary>
	public static Vector3 AdjustBulletDirection(BulletTableItem bulletRes, BattleUnit firer, Vector3 startPosition, Vector3 targetPosition)
	{
		// 几种类型的子弹, 沿着开火者的朝向飞行(这几种子弹按方向飞行)
		if (NeedAdjustTargetDirection(bulletRes.type))
		{
			// 处理起点与终点重合的特殊情况.
			float dist = Mathf.Max(0.1f, Utility.Distance2D(firer.GetPosition(), targetPosition));
			float dir = firer.GetDirection();
			return Utility.MoveVector3Towards(startPosition, dir, dist);
		}

		// 其他类型飞到目标点.
		return targetPosition;
	}

	private static bool NeedAdjustTargetPosition(BulletType type)
	{
		return (type == BulletType.BulletTypeBasic
			|| type == BulletType.BulletTypeMissile
			|| type == BulletType.BulletTypeRocket
			|| type >= BulletType.BulletTypeFlyAlongEquationGraph);
	}

	private static bool NeedAdjustTargetDirection(BulletType type)
	{
		return (type == BulletType.BulletTypeBasic
			|| type == BulletType.BulletTypeMissile
			|| type == BulletType.BulletTypeRocket
			|| type >= BulletType.BulletTypeFlyAlongEquationGraph);
	}
}

public interface BulletAllocator
{
	Bullet Allocate();
}

public class BulletFactory
{
	static BulletFactory instance = new BulletFactory();
	static public BulletFactory Instance { get { return instance; } }
	BulletAllocator[] allocators = new BulletAllocator[(int)BulletType.BulletTypeCount];

	BulletFactory()
	{
		allocators[(int)BulletType.BulletTypeBasic] = new BulletBasic.Allocator();
		allocators[(int)BulletType.BulletTypeCannonball] = new BulletCannonball.Allocator();
		allocators[(int)BulletType.BulletTypeGrenade] = new BulletGrenade.Allocator();
		allocators[(int)BulletType.BulletTypeBomb] = new BulletBomb.Allocator();
		allocators[(int)BulletType.BulletTypeMissile] = new BulletMissile.Allocator();
		allocators[(int)BulletType.BulletTypeRocket] = new BulletRocket.Allocator();
		allocators[(int)BulletType.BulletTypeCircleArc] = new BulletEquationalCircleArc.Allocator();
		allocators[(int)BulletType.BulletTypeSineWave] = new BulletEquationalSineWave.Allocator();
	}

	public Bullet Allocate(BulletType type)
	{
		if (type >= BulletType.BulletTypeCount)
		{
			ErrorHandler.Parse(ErrorCode.LogicError, "invalid bullet type");
			return null;
		}

		return allocators[(int)type].Allocate();
	}
}

#endregion Helpers
