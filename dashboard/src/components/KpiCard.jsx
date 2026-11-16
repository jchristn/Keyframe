import { Link } from 'react-router-dom';
import Icon from './Icon';

/** KPI tile: icon and label, a large value, an optional sub line, and an optional link. */
function KpiCard({ label, value, sub = null, to = null, tone = null, icon, title = null }) {
  const body = (
    <>
      <div className="kpi-label">
        <Icon name={icon} />
        <span>{label}</span>
      </div>
      <div className={'kpi-value' + (tone ? ' text-' + tone : '')} title={title || undefined}>
        {value}
      </div>
      {sub ? <div className="kpi-sub">{sub}</div> : null}
    </>
  );
  return to ? (
    <Link to={to} className="kpi-card clickable">
      {body}
    </Link>
  ) : (
    <div className="kpi-card">{body}</div>
  );
}

export default KpiCard;
